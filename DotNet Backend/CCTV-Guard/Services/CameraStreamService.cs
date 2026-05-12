using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Channels;
using CCTV_Guard.Data;
using CCTV_Guard.Hubs;
using CCTV_Guard.Models.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace CCTV_Guard.Services;

/// <summary>
/// Camera streaming service — clean architecture:
///
///   FFmpeg → ParseFramesAsync → Channel<byte[]> → SendFramesAsync → SignalR clients
///                                                        ↓ (every Nth frame, fire-and-forget)
///                                                  ProcessAiDetectionAsync (isolated Task)
///
/// KEY DESIGN RULES:
///   1. SendFramesAsync NEVER awaits anything except the channel read.
///      All DB calls are done on background tasks, never blocking the video pipeline.
///   2. AI detection is 100% fire-and-forget — video speed is never affected by AI speed.
///   3. Detection/settings flags are cached in memory — zero DB calls in the hot path.
/// </summary>
public class CameraStreamService : IDisposable
{
    private readonly ConcurrentDictionary<string, StreamState> _streams = new();
    private readonly IHubContext<CameraStreamHub> _hub;
    private readonly ILogger<CameraStreamService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;

    private static string? _ffmpegExePath;
    private static bool    _ffmpegReady = false;
    private static readonly SemaphoreSlim _ffmpegInitLock = new(1, 1);

    // ── In-memory settings cache — refreshed by background timer, NEVER in hot path ──
    private volatile bool _weaponDetection    = true;
    private volatile bool _fightDetection     = true;
    private volatile bool _intrusionDetection = true;
    private volatile bool _faceRecognition    = true;
    private volatile bool _licensePlate       = true;

    // Per-camera detection-enabled flag — updated by background timer
    private readonly ConcurrentDictionary<string, bool> _detectionEnabled = new();

    // Background settings refresh timer
    private Timer? _settingsTimer;

    // Per-camera AI semaphore — 1 AI call in-flight per camera max
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _aiSemaphores = new();

    // Incident deduplication — 30s cooldown per camera+label
    private readonly ConcurrentDictionary<string, DateTime> _lastIncidentTime = new();
    private static readonly TimeSpan IncidentCooldown = TimeSpan.FromSeconds(30);

    // Send every Nth frame to AI — channel capacity=2 so parser never blocks
    // AI semaphore ensures only 1 call in-flight; extras are dropped harmlessly
    private const int AiFrameInterval = 10;

    public string? FfmpegExePath => _ffmpegExePath;

    public CameraStreamService(
        IHubContext<CameraStreamHub> hub,
        ILogger<CameraStreamService> logger,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory)
    {
        _hub               = hub;
        _logger            = logger;
        _scopeFactory      = scopeFactory;
        _httpClientFactory = httpClientFactory;

        // Refresh settings every 15s in background — never blocks the video pipeline
        _settingsTimer = new Timer(_ => _ = RefreshSettingsAsync(), null,
            TimeSpan.Zero, TimeSpan.FromSeconds(15));
    }

    // ── Settings refresh — runs on timer thread, never in video hot path ─────

    private async Task RefreshSettingsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var s = await db.AiSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1);
            if (s != null)
            {
                _weaponDetection    = s.WeaponDetection;
                _fightDetection     = s.FightDetection;
                _intrusionDetection = s.IntrusionDetection;
                _faceRecognition    = s.FaceRecognition;
                _licensePlate       = s.LicensePlate;
            }

            var cameras = await db.Cameras.AsNoTracking()
                .Select(c => new { c.Id, c.DetectionEnabled })
                .ToListAsync();
            foreach (var c in cameras)
                _detectionEnabled[c.Id] = c.DetectionEnabled;
        }
        catch { /* keep previous values on error */ }
    }

    // ── FFmpeg bootstrap ──────────────────────────────────────────────────────

    public async Task EnsureFfmpegAsync()
    {
        if (_ffmpegReady) return;
        await _ffmpegInitLock.WaitAsync();
        try
        {
            if (_ffmpegReady) return;
            var ffmpegDir  = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg-bin");
            Directory.CreateDirectory(ffmpegDir);
            var exeName    = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            _ffmpegExePath = Path.Combine(ffmpegDir, exeName);
            if (!File.Exists(_ffmpegExePath))
            {
                _logger.LogInformation("FFmpeg not found — downloading (~70 MB, one-time)...");
                FFmpeg.SetExecutablesPath(ffmpegDir);
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegDir);
                _logger.LogInformation("FFmpeg downloaded successfully.");
            }
            _ffmpegReady = true;
        }
        finally { _ffmpegInitLock.Release(); }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task StartAsync(string cameraId)
    {
        if (_streams.TryGetValue(cameraId, out var existing) && !existing.Cts.IsCancellationRequested)
            return;

        var streamUrl = await GetStreamUrlAsync(cameraId);
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            _logger.LogWarning("Camera {Id}: no stream URL configured.", cameraId);
            return;
        }

        await EnsureFfmpegAsync();

        var cts = new CancellationTokenSource();
        _streams.AddOrUpdate(cameraId, new StreamState(cts), (_, prev) =>
        {
            try { prev.Cts.Cancel(); prev.Cts.Dispose(); } catch { }
            return new StreamState(cts);
        });

        _ = Task.Run(() => RunStreamLoopAsync(cameraId, streamUrl, cts.Token), cts.Token);
        await UpdateCameraStatusAsync(cameraId, "online");
        _logger.LogInformation("Camera {Id}: stream started.", cameraId);
    }

    public async Task StopAsync(string cameraId)
    {
        if (_streams.TryRemove(cameraId, out var state))
        {
            state.Cts.Cancel();
            state.Cts.Dispose();
        }
        await UpdateCameraStatusAsync(cameraId, "offline");
        try { await _hub.Clients.Group($"camera-{cameraId}").SendAsync("CameraOffline", new { cameraId }); }
        catch { }
        _logger.LogInformation("Camera {Id}: stream stopped.", cameraId);
    }

    public bool IsStreaming(string cameraId) =>
        _streams.TryGetValue(cameraId, out var s) && !s.Cts.IsCancellationRequested;

    // ── Core streaming loop ───────────────────────────────────────────────────

    private async Task RunStreamLoopAsync(string cameraId, string streamUrl, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Process? process = null;
            try
            {
                process = StartFfmpegProcess(streamUrl);
                await UpdateCameraStatusAsync(cameraId, "online");

                // Channel capacity=2: parser can stay 1 frame ahead of sender.
                // DropOldest ensures we always send the freshest frame.
                var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2)
                {
                    FullMode     = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = true,
                });
                var parseTask = Task.Run(() => ParseFramesAsync(process, channel.Writer, ct), ct);
                var sendTask  = Task.Run(() => SendFramesAsync(cameraId, channel.Reader, ct), ct);

                try   { await Task.WhenAll(parseTask, sendTask); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _logger.LogWarning(ex, "Camera {Id}: pipeline error.", cameraId); throw; }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Camera {Id}: stream error, retrying in 2s...", cameraId);
                await UpdateCameraStatusAsync(cameraId, "error");
                try { await Task.Delay(2000, ct); } catch { break; }
            }
            finally
            {
                if (process != null)
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                    process.Dispose();
                }
            }
        }
    }

    private Process StartFfmpegProcess(string streamUrl)
    {
        var isRtmp = streamUrl.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase);
        var isHttp = streamUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                  || streamUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        string inputArgs;
        if (isHttp)
        {
            // IP Webcam / HTTP MJPEG — reconnect flags keep stream alive
            inputArgs = string.Join(" ",
                "-fflags nobuffer+discardcorrupt",
                "-flags low_delay",
                "-reconnect 1",
                "-reconnect_streamed 1",
                "-reconnect_delay_max 2",
                $"-i \"{streamUrl}\"");
        }
        else if (isRtmp)
        {
            inputArgs = $"-fflags nobuffer+discardcorrupt -flags low_delay -i \"{streamUrl}\"";
        }
        else
        {
            // RTSP — TCP transport, zero delay
            inputArgs = string.Join(" ",
                "-rtsp_transport tcp",
                "-fflags nobuffer+discardcorrupt",
                "-flags low_delay",
                "-max_delay 0",
                $"-i \"{streamUrl}\"");
        }

        // -vsync drop: drop frames to stay real-time, never buffer
        var args = string.Join(" ",
            "-loglevel warning",
            inputArgs,
            "-vf fps=15,scale=640:480",
            "-vsync drop",
            "-vcodec mjpeg",
            "-q:v 4",
            "-f image2pipe",
            "pipe:1"
        );

        var psi = new ProcessStartInfo
        {
            FileName               = _ffmpegExePath!,
            Arguments              = args,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        var process = new Process { StartInfo = psi };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger.LogDebug("FFmpeg: {Line}", e.Data);
        };
        process.Start();
        process.BeginErrorReadLine();
        return process;
    }

    // ── Frame parser — reads FFmpeg stdout, extracts complete JPEGs ───────────

    private async Task ParseFramesAsync(Process process, ChannelWriter<byte[]> writer, CancellationToken ct)
    {
        var stream = process.StandardOutput.BaseStream;
        var buf    = new byte[65536];
        var ms     = new MemoryStream(64 * 1024);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n;
                try   { n = await stream.ReadAsync(buf, 0, buf.Length, ct); }
                catch { break; }
                if (n == 0) break;

                ms.Write(buf, 0, n);

                var data = ms.GetBuffer();
                var len  = (int)ms.Length;
                var pos  = 0;

                while (pos < len - 1)
                {
                    // Find JPEG SOI marker FF D8
                    int soi = -1;
                    for (int i = pos; i < len - 1; i++)
                        if (data[i] == 0xFF && data[i + 1] == 0xD8) { soi = i; break; }
                    if (soi < 0) break;

                    // Find JPEG EOI marker FF D9
                    int eoi = -1;
                    for (int i = soi + 2; i < len - 1; i++)
                        if (data[i] == 0xFF && data[i + 1] == 0xD9) { eoi = i + 1; break; }
                    if (eoi < 0) break;

                    int frameLen = eoi - soi + 1;
                    if (frameLen >= 5000)
                    {
                        var jpeg = new byte[frameLen];
                        Buffer.BlockCopy(data, soi, jpeg, 0, frameLen);
                        // TryWrite — non-blocking. Channel drops oldest if full (DropOldest mode).
                        writer.TryWrite(jpeg);
                    }
                    pos = eoi + 1;
                }

                // Compact buffer
                if (pos > 0)
                {
                    var rem = len - pos;
                    if (rem > 0) Buffer.BlockCopy(data, pos, data, 0, rem);
                    ms.SetLength(rem);
                    ms.Position = rem;
                }

                // Safety reset if buffer grows too large
                if (ms.Length > 2 * 1024 * 1024)
                {
                    ms.SetLength(0);
                    ms.Position = 0;
                }
            }
        }
        finally { writer.Complete(); }
    }

    // ── Frame sender — PURE hot path, zero DB calls, zero locks ──────────────

    private async Task SendFramesAsync(string cameraId, ChannelReader<byte[]> reader, CancellationToken ct)
    {
        var frameNum = 0;

        await foreach (var jpeg in reader.ReadAllAsync(ct))
        {
            frameNum++;

            // Send frame to all viewers — fire-and-forget, never await
            var b64 = Convert.ToBase64String(jpeg);
            var ts  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _ = _hub.Clients.Group($"camera-{cameraId}")
                .SendAsync("ReceiveFrame", new { cameraId, frame = b64, frameNum, timestamp = ts },
                    CancellationToken.None);

            // AI detection — every Nth frame, completely isolated
            if (frameNum % AiFrameInterval != 0) continue;

            // Check detection flag from in-memory cache — zero DB calls
            if (!_detectionEnabled.GetValueOrDefault(cameraId, true)) continue;

            var sem = _aiSemaphores.GetOrAdd(cameraId, _ => new SemaphoreSlim(1, 1));
            if (!sem.Wait(0)) continue; // AI busy — drop this frame, video unaffected

            var capturedJpeg = jpeg;
            _ = Task.Run(async () =>
            {
                try   { await ProcessAiDetectionAsync(cameraId, capturedJpeg); }
                finally { sem.Release(); }
            });
        }
    }

    // ── AI Detection — runs on thread pool, never touches video pipeline ──────

    private async Task ProcessAiDetectionAsync(string cameraId, byte[] jpeg)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("AiService");
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(cameraId), "camera_id");
            content.Add(new ByteArrayContent(jpeg)
            {
                Headers = { ContentType = MediaTypeHeaderValue.Parse("image/jpeg") }
            }, "file", "frame.jpg");
            content.Add(new StringContent("0.25"), "confidence_threshold");
            content.Add(new StringContent(_faceRecognition ? "true" : "false"), "run_face_recognition");

            using var response = await client.PostAsync("/detect", content);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Camera {Id}: AI returned {Code}", cameraId, response.StatusCode);
                return;
            }

            var json   = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<AiDetectResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.Detections == null || result.Detections.Count == 0) return;

            // Filter by enabled modules (in-memory flags — no DB)
            var filtered = result.Detections.Where(d => d.Label switch
            {
                "weapon"        => _weaponDetection,
                "fight"         => _fightDetection,
                "fire"          => true,
                "intrusion"     => _intrusionDetection,
                "unknown_face"  => _faceRecognition,
                "person"        => true,
                "license_plate" => _licensePlate,
                _               => true
            }).ToList();

            if (filtered.Count == 0) return;

            _logger.LogInformation("Camera {Id}: {Count} detection(s): {Labels}",
                cameraId, filtered.Count,
                string.Join(", ", filtered.Select(d => $"{d.Label}({d.Confidence:P0})")));

            // Push bounding boxes — only sent when detections exist
            _ = _hub.Clients.Group($"camera-{cameraId}")
                .SendAsync("BoundingBoxes", new
                {
                    cameraId,
                    detections = filtered.Select(d => new
                    {
                        label      = d.Label,
                        confidence = d.Confidence,
                        severity   = d.Severity,
                        x          = d.BoundingBox.X,
                        y          = d.BoundingBox.Y,
                        w          = d.BoundingBox.W,
                        h          = d.BoundingBox.H
                    })
                }, CancellationToken.None);

            // Create incidents for detections above threshold
            foreach (var det in filtered)
            {
                float minConf = det.Label switch
                {
                    "weapon"        => 0.40f,
                    "fire"          => 0.80f,
                    "fight"         => 0.60f,
                    "unknown_face"  => 0.55f,
                    "intrusion"     => 0.45f,
                    "license_plate" => 0.35f,
                    _               => 0.40f
                };
                if (det.Confidence >= minConf)
                    await CreateIncidentAsync(cameraId, det, jpeg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Camera {Id}: AI detection error — {Msg}", cameraId, ex.Message);
        }
    }

    private async Task CreateIncidentAsync(string cameraId, AiDetection det, byte[] jpeg)
    {
        var dedupeKey = $"{cameraId}:{det.Label}:{det.YoloClass}";
        var now       = DateTime.UtcNow;
        if (_lastIncidentTime.TryGetValue(dedupeKey, out var last) && now - last < IncidentCooldown)
            return;
        _lastIncidentTime[dedupeKey] = now;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var incidentId   = "inc-" + Guid.NewGuid().ToString("N")[..8];
            var alertId      = "alr-" + Guid.NewGuid().ToString("N")[..8];
            var message      = $"{det.Label} detected with {det.Confidence:P0} confidence.";
            var thumbnailUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(jpeg)}";

            db.Incidents.Add(new Incident
            {
                Id           = incidentId,
                CameraId     = cameraId,
                Type         = det.Label,
                Severity     = det.Severity,
                Confidence   = (decimal)det.Confidence,
                Timestamp    = now,
                ThumbnailUrl = thumbnailUrl,
                BoundingBoxX = det.BoundingBox.X,
                BoundingBoxY = det.BoundingBox.Y,
                BoundingBoxW = det.BoundingBox.W,
                BoundingBoxH = det.BoundingBox.H,
                Status       = "new"
            });

            var alert = new Alert
            {
                Id         = alertId,
                IncidentId = incidentId,
                CameraId   = cameraId,
                Type       = $"{det.Label} Detected",
                Message    = message,
                Severity   = det.Severity,
                Timestamp  = now
            };
            db.Alerts.Add(alert);
            await db.SaveChangesAsync();

            var cam       = await db.Cameras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cameraId);
            var hubNotify = scope.ServiceProvider.GetRequiredService<HubNotificationService>();
            await hubNotify.SendNewAlertAsync(alert, cam?.Name ?? cameraId);

            _logger.LogInformation("Camera {Id}: incident created — {Type} {Conf:P0}",
                cameraId, det.Label, det.Confidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Camera {Id}: failed to create incident for {Type}", cameraId, det.Label);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string?> GetStreamUrlAsync(string cameraId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Cameras.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cameraId))?.RtspUrl;
    }

    private async Task UpdateCameraStatusAsync(string cameraId, string status)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cam = await db.Cameras.FirstOrDefaultAsync(c => c.Id == cameraId);
            if (cam == null) return;
            cam.Status   = status;
            cam.LastSeen = status == "online" ? DateTime.UtcNow : cam.LastSeen;
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Camera {Id}: status update failed.", cameraId); }
    }

    public void Dispose()
    {
        _settingsTimer?.Dispose();
        foreach (var (_, s) in _streams)
            try { s.Cts.Cancel(); s.Cts.Dispose(); } catch { }
        _streams.Clear();
    }

    private record StreamState(CancellationTokenSource Cts);

    // ── AI response DTOs ──────────────────────────────────────────────────────

    private class AiDetectResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("camera_id")]
        public string CameraId { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("detections")]
        public List<AiDetection> Detections { get; set; } = new();
    }

    private class AiDetection
    {
        [System.Text.Json.Serialization.JsonPropertyName("label")]
        public string Label { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("yolo_class")]
        public string YoloClass { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("confidence")]
        public float Confidence { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("severity")]
        public string Severity { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("bounding_box")]
        public AiBoundingBox BoundingBox { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("face_username")]
        public string? FaceUsername { get; set; }
    }

    private class AiBoundingBox
    {
        [System.Text.Json.Serialization.JsonPropertyName("x")] public int X { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("y")] public int Y { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("w")] public int W { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("h")] public int H { get; set; }
    }
}
