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
/// High-performance camera streaming service.
/// Architecture:
///   FFmpeg → stdout pipe → frame parser → Channel<byte[]> → SignalR sender
///
/// The frame parser and SignalR sender run on separate tasks via a Channel,
/// so parsing never blocks sending and sending never blocks parsing.
/// AI detection runs completely independently on a separate thread pool task.
/// </summary>
public class CameraStreamService : IDisposable
{
    private readonly ConcurrentDictionary<string, StreamState> _streams = new();
    private readonly IHubContext<CameraStreamHub> _hub;
    private readonly ILogger<CameraStreamService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;

    private static string? _ffmpegExePath;
    private static bool _ffmpegReady = false;
    private static readonly SemaphoreSlim _ffmpegInitLock = new(1, 1);

    // Cache AI settings — refreshed every 30s so toggle changes take effect quickly
    private AiSettingsCache _aiSettingsCache = new();
    private DateTime _aiSettingsCacheExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _aiSettingsCacheLock = new(1, 1);

    private record AiSettingsCache(
        bool FightDetection     = true,
        bool WeaponDetection    = true,
        bool IntrusionDetection = true,
        bool FaceRecognition    = true,
        bool LicensePlate       = true
    );

    private async Task<AiSettingsCache> GetAiSettingsAsync()
    {
        if (DateTime.UtcNow < _aiSettingsCacheExpiry) return _aiSettingsCache;
        await _aiSettingsCacheLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow < _aiSettingsCacheExpiry) return _aiSettingsCache;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s  = await db.AiSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1);
            if (s != null)
                _aiSettingsCache = new AiSettingsCache(
                    s.FightDetection, s.WeaponDetection, s.IntrusionDetection,
                    s.FaceRecognition, s.LicensePlate);
            _aiSettingsCacheExpiry = DateTime.UtcNow.AddSeconds(30);
        }
        catch { /* keep previous cache on DB error */ }
        finally { _aiSettingsCacheLock.Release(); }
        return _aiSettingsCache;
    }

    // Cache confidence thresholds — refreshed every 60s, avoids N+1 DB queries per AI frame
    private readonly ConcurrentDictionary<string, decimal> _confidenceCache = new();
    private DateTime _confidenceCacheExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _confidenceCacheLock = new(1, 1);

    public string? FfmpegExePath => _ffmpegExePath;

    // AI: send 1 frame per second to avoid overloading the AI service
    // At 15fps video, every 15th frame = exactly 1fps to AI
    private const int AiFrameInterval = 15;

    // Circuit breaker for AI service — per-instance, not shared across cameras
    private bool _aiServiceAvailable = true;
    private DateTime _aiNextRetryTime = DateTime.MinValue;

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
            else
            {
                _logger.LogInformation("FFmpeg found at {Path}", _ffmpegExePath);
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
        var newState = new StreamState(cts);

        // Atomically replace — dispose old CTS if it existed
        _streams.AddOrUpdate(cameraId, newState, (_, prev) =>
        {
            try { prev.Cts.Cancel(); prev.Cts.Dispose(); } catch { /* ignore */ }
            return newState;
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
        try
        {
            await _hub.Clients.Group($"camera-{cameraId}")
                .SendAsync("CameraOffline", new { cameraId });
        }
        catch { /* ignore */ }
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

                // FFmpeg started — mark camera online
                await UpdateCameraStatusAsync(cameraId, "online");

                // Channel decouples frame parsing from SignalR sending.
                // Capacity=1: always drop old frames, only send the very latest.
                // This is the key to zero-lag live video — never queue up stale frames.
                var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
                {
                    FullMode        = BoundedChannelFullMode.DropOldest,
                    SingleReader    = true,
                    SingleWriter    = true,
                });

                // Task 1: parse frames from FFmpeg stdout → write to channel
                var parseTask = Task.Run(() => ParseFramesAsync(process, channel.Writer, ct), ct);

                // Task 2: read from channel → send via SignalR
                var sendTask  = Task.Run(() => SendFramesAsync(cameraId, channel.Reader, ct), ct);

                try
                {
                    await Task.WhenAll(parseTask, sendTask);
                }
                catch (OperationCanceledException) { throw; } // let outer catch handle it
                catch (Exception ex)
                {
                    // One of the tasks failed — log and let the outer loop retry
                    _logger.LogWarning(ex, "Camera {Id}: pipeline task failed.", cameraId);
                    throw;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Camera {Id}: stream error, retrying in 1s...", cameraId);
                await UpdateCameraStatusAsync(cameraId, "error");
                try { await Task.Delay(1000, ct); } catch { break; }
                // Status stays "error" until FFmpeg reconnects and frames start flowing
            }
            finally
            {
                if (process != null)
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                    catch { /* ignore */ }
                    process.Dispose();
                }
            }
        }
    }

    private Process StartFfmpegProcess(string streamUrl)
    {
        var isRtmp = streamUrl.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase);

        // Aggressive low-latency flags:
        //   probesize 32 + analyzeduration 0  → skip stream analysis, start immediately
        //   fflags nobuffer + avioflags direct → no I/O buffering at all
        //   flags low_delay                    → decoder low-delay mode
        //   max_delay 0                        → zero mux delay
        //   flush_packets 1                    → flush every packet immediately
        var inputArgs = isRtmp
            ? $"-probesize 32 -analyzeduration 0 -avioflags direct -fflags nobuffer -flags low_delay -max_delay 0 -flush_packets 1 -i \"{streamUrl}\""
            : $"-probesize 32 -analyzeduration 0 -avioflags direct -fflags nobuffer -flags low_delay -max_delay 0 -flush_packets 1 -rtsp_transport tcp -i \"{streamUrl}\"";

        // 15fps is enough for live monitoring and reduces pipeline backpressure vs 25fps
        // q:v 5 = slightly lower quality but faster encode → less latency
        var args = string.Join(" ",
            "-loglevel warning",
            inputArgs,
            "-vf fps=15,scale=640:480",
            "-vcodec mjpeg",
            "-q:v 5",
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

        // Increase stdout buffer size for smoother reading
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

    private async Task ParseFramesAsync(
        Process process,
        ChannelWriter<byte[]> writer,
        CancellationToken ct)
    {
        var stream = process.StandardOutput.BaseStream;
        // Small read buffer — we want to process data as soon as it arrives, not batch it
        var buf    = new byte[65536];
        var ms     = new MemoryStream(32 * 1024); // 32KB — just enough for one JPEG frame

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int bytesRead;
                try { bytesRead = await stream.ReadAsync(buf, 0, buf.Length, ct); }
                catch (OperationCanceledException) { break; }
                if (bytesRead == 0) break;

                ms.Write(buf, 0, bytesRead);

                // Scan the buffer for complete JPEG frames
                var data = ms.GetBuffer();
                var len  = (int)ms.Length;
                var pos  = 0;

                while (pos < len - 1)
                {
                    // Find SOI (FF D8)
                    int soiIdx = -1;
                    for (int i = pos; i < len - 1; i++)
                    {
                        if (data[i] == 0xFF && data[i + 1] == 0xD8)
                        { soiIdx = i; break; }
                    }
                    if (soiIdx < 0) break;

                    // Find EOI (FF D9) after SOI
                    int eoiIdx = -1;
                    for (int i = soiIdx + 2; i < len - 1; i++)
                    {
                        if (data[i] == 0xFF && data[i + 1] == 0xD9)
                        { eoiIdx = i + 1; break; }
                    }
                    if (eoiIdx < 0) break; // incomplete frame — wait for more data

                    int frameLen = eoiIdx - soiIdx + 1;
                    if (frameLen >= 5000) // skip tiny/corrupt frames
                    {
                        var jpeg = new byte[frameLen];
                        Buffer.BlockCopy(data, soiIdx, jpeg, 0, frameLen);
                        await writer.WriteAsync(jpeg, ct); // Channel handles backpressure
                    }

                    pos = eoiIdx + 1;
                }

                // Compact the buffer — keep only unprocessed bytes
                if (pos > 0)
                {
                    var remaining = len - pos;
                    if (remaining > 0)
                        Buffer.BlockCopy(data, pos, data, 0, remaining);
                    ms.SetLength(remaining);
                    ms.Position = remaining;
                }

                // Safety: if buffer grows > 2 MB without a complete frame, reset
                if (ms.Length > 2 * 1024 * 1024)
                {
                    _logger.LogWarning("Frame parser: buffer overflow, resetting");
                    ms.SetLength(0);
                    ms.Position = 0;
                }
            }
        }
        finally
        {
            writer.Complete();
        }
    }

    // ── Frame sender — reads from channel, sends via SignalR ─────────────────

    private async Task SendFramesAsync(
        string cameraId,
        ChannelReader<byte[]> reader,
        CancellationToken ct)
    {
        var frameNum = 0;

        await foreach (var jpeg in reader.ReadAllAsync(ct))
        {
            frameNum++;
            var base64 = Convert.ToBase64String(jpeg);

            try
            {
                // Fire-and-forget — don't await, prevents head-of-line blocking
                // Wrap in a task so exceptions are caught and logged, not silently swallowed
                _ = _hub.Clients.Group($"camera-{cameraId}")
                    .SendAsync("ReceiveFrame", new
                    {
                        cameraId,
                        frame    = base64,
                        frameNum,
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }, CancellationToken.None)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            _logger.LogDebug("Camera {Id}: SignalR send failed (client likely disconnected)", cameraId);
                    }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch { /* ignore sync exceptions */ }

            // AI detection — completely isolated, never affects video.
            // TryWait(0) = non-blocking: if AI is busy with previous frame, skip this one.
            if (frameNum % AiFrameInterval == 0)
            {
                // Check if detection is enabled for this camera before doing any work
                var detectionEnabled = await IsCameraDetectionEnabledAsync(cameraId);
                if (!detectionEnabled) continue;

                var sem = _aiSemaphores.GetOrAdd(cameraId, _ => new SemaphoreSlim(1, 1));
                if (sem.Wait(0)) // non-blocking — returns false immediately if busy
                {
                    var frameForAi = jpeg; // capture for closure
                    _ = Task.Run(async () =>
                    {
                        try   { await ProcessAiDetectionAsync(cameraId, frameForAi, CancellationToken.None); }
                        finally { sem.Release(); }
                    });
                }
                // else: AI still processing previous frame — drop this one, video unaffected
            }
        }
    }

    // ── AI Detection ──────────────────────────────────────────────────────────

    private async Task ProcessAiDetectionAsync(string cameraId, byte[] jpeg, CancellationToken ct)
    {
        if (!_aiServiceAvailable && DateTime.UtcNow < _aiNextRetryTime) return;

        // Read current AI module settings — cached for 30s
        var aiSettings = await GetAiSettingsAsync();

        try
        {
            var client = _httpClientFactory.CreateClient("AiService");
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(cameraId), "camera_id");
            content.Add(new ByteArrayContent(jpeg)
            {
                Headers = { ContentType = MediaTypeHeaderValue.Parse("image/jpeg") }
            }, "file", "frame.jpg");

            var aiThreshold = 0.25m;
            content.Add(new StringContent(aiThreshold.ToString("F2")), "confidence_threshold");

            // Pass face recognition flag from AiSettings to the Python service
            content.Add(new StringContent(aiSettings.FaceRecognition.ToString().ToLower()), "run_face_recognition");

            var response = await client.PostAsync("/detect", content, ct);
            if (!response.IsSuccessStatusCode) return;

            _aiServiceAvailable = true;

            var json = await response.Content.ReadAsStringAsync(ct);
            _logger.LogDebug("Camera {Id}: AI raw response: {Json}", cameraId, json[..Math.Min(500, json.Length)]);

            AiDetectResponse? result;
            try
            {
                result = JsonSerializer.Deserialize<AiDetectResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Camera {Id}: failed to deserialize AI response", cameraId);
                return;
            }

            if (result?.Detections == null || result.Detections.Count == 0)
            {
                _logger.LogDebug("Camera {Id}: no detections after deserialization", cameraId);
                return;
            }

            // Filter detections by enabled AI modules
            var filtered = result.Detections.Where(d =>
            {
                return d.Label switch
                {
                    "weapon"        => aiSettings.WeaponDetection,
                    "fight"         => aiSettings.FightDetection,
                    "fire"          => true,  // fire always on — no separate toggle
                    "intrusion"     => aiSettings.IntrusionDetection,
                    "unknown_face"  => aiSettings.FaceRecognition,
                    "person"        => true,  // always show persons (low severity, no alert)
                    "license_plate" => aiSettings.LicensePlate,
                    _               => true
                };
            }).ToList();

            // Log ALL enabled detections
            foreach (var d in filtered)
                _logger.LogInformation("Camera {Id}: detected {Label} ({YoloClass}) conf={Conf:P0} severity={Sev}",
                    cameraId, d.Label, d.YoloClass, d.Confidence, d.Severity);

            // Push bounding boxes for enabled detections only
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

            // Create incidents for enabled detections only.
            // Per-label confidence gates — calibrated to eliminate false positives:
            //   weapon:  ≥ 70% — threat model (knife/gun/grenade) only at high confidence
            //   fire:    ≥ 85% — fire model has false positives with warm brick surfaces
            //   fight:   ≥ 65% — pose-based, needs high confidence to avoid false positives
            //   unknown_face: ≥ 60% — face model is reliable above this threshold
            //   intrusion:    ≥ 50% — person detection, lowered to catch real persons
            //   license_plate: ≥ 40% — COCO motorcycle/car detection is 40-65% range
            foreach (var det in filtered.Where(d =>
            {
                return (d.Label, d.Severity) switch
                {
                    ("weapon",        _)          => d.Confidence >= 0.70f,
                    ("fire",          _)          => d.Confidence >= 0.85f,
                    ("fight",         _)          => d.Confidence >= 0.65f,
                    ("unknown_face",  _)          => d.Confidence >= 0.60f,
                    ("intrusion",     _)          => d.Confidence >= 0.50f,
                    ("license_plate", _)          => d.Confidence >= 0.40f,
                    (_,               "critical") => d.Confidence >= 0.70f,
                    (_,               "high")     => d.Confidence >= 0.60f,
                    (_,               "medium")   => d.Confidence >= 0.50f,
                    (_,               "low")      => d.Confidence >= 0.40f,
                    _                             => false
                };
            }))
                await CreateIncidentAsync(cameraId, det, jpeg, ct);
        }
        catch (HttpRequestException)
        {
            _aiServiceAvailable = false;
            _aiNextRetryTime    = DateTime.UtcNow.AddSeconds(30);
        }
        catch (Exception ex) when (ex is TaskCanceledException || ex.InnerException is TaskCanceledException)
        {
            _aiServiceAvailable = false;
            _aiNextRetryTime    = DateTime.UtcNow.AddSeconds(30);
        }
        catch { /* ignore other errors */ }
    }

    private async Task CreateIncidentAsync(string cameraId, AiDetection det, byte[] jpeg, CancellationToken ct)
    {
        // Deduplication: skip if same camera+label+yolo_class was logged within the cooldown window.
        // Use yolo_class (e.g. "Gun", "knife") not just label ("weapon") so different weapon types
        // each get their own cooldown and don't block each other.
        var dedupeKey = $"{cameraId}:{det.Label}:{det.YoloClass}";
        var timestamp = DateTime.UtcNow;
        if (_lastIncidentTime.TryGetValue(dedupeKey, out var lastTime) &&
            timestamp - lastTime < IncidentCooldown)
            return;
        _lastIncidentTime[dedupeKey] = timestamp;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var incidentId = "inc-" + Guid.NewGuid().ToString("N")[..8];
            var alertId    = "alr-" + Guid.NewGuid().ToString("N")[..8];

            // Build a descriptive message — include face name if recognised
            var message = det.FaceUsername != null
                ? $"{det.Label} detected: {det.FaceUsername} (confidence {det.Confidence:P0})"
                : $"{det.Label} detected with {det.Confidence:P0} confidence.";

            // Save the JPEG frame as a base64 data URL for the incident thumbnail
            var thumbnailUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(jpeg)}";

            db.Incidents.Add(new Incident
            {
                Id           = incidentId,
                CameraId     = cameraId,
                Type         = det.Label,
                Severity     = det.Severity,
                Confidence   = (decimal)det.Confidence,
                Timestamp    = timestamp,
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
                Timestamp  = timestamp
            };
            db.Alerts.Add(alert);
            await db.SaveChangesAsync(ct);

            var cam       = await db.Cameras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cameraId, ct);
            var hubNotify = scope.ServiceProvider.GetRequiredService<HubNotificationService>();
            await hubNotify.SendNewAlertAsync(alert, cam?.Name ?? cameraId);

            _logger.LogInformation("Camera {Id}: {Type} ({Confidence:P0}) → incident {Inc}",
                cameraId, det.Label, det.Confidence, incidentId);
        }
        catch (Exception ex)
        {
            // Log the FULL exception — silent failures here mean alerts never fire
            _logger.LogError(ex, "Camera {Id}: FAILED to create incident for {Type}. Exception: {Msg}",
                cameraId, det.Label, ex.Message);
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

    // Cache per-camera detection enabled flag — refreshed every 30s
    private readonly ConcurrentDictionary<string, bool> _detectionEnabledCache = new();
    private DateTime _detectionCacheExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _detectionCacheLock = new(1, 1);

    private async Task<bool> IsCameraDetectionEnabledAsync(string cameraId)
    {
        if (DateTime.UtcNow < _detectionCacheExpiry &&
            _detectionEnabledCache.TryGetValue(cameraId, out var cached))
            return cached;

        await _detectionCacheLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow < _detectionCacheExpiry &&
                _detectionEnabledCache.TryGetValue(cameraId, out var cached2))
                return cached2;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cameras = await db.Cameras.AsNoTracking()
                .Select(c => new { c.Id, c.DetectionEnabled })
                .ToListAsync();
            _detectionEnabledCache.Clear();
            foreach (var c in cameras)
                _detectionEnabledCache[c.Id] = c.DetectionEnabled;
            _detectionCacheExpiry = DateTime.UtcNow.AddSeconds(30);
        }
        catch { /* keep previous cache */ }
        finally { _detectionCacheLock.Release(); }

        return _detectionEnabledCache.TryGetValue(cameraId, out var result) && result;
    }

    private async Task<decimal> GetConfidenceThresholdAsync(string cameraId)
    {
        // Refresh cache every 60 seconds
        if (DateTime.UtcNow > _confidenceCacheExpiry)
        {
            await _confidenceCacheLock.WaitAsync();
            try
            {
                if (DateTime.UtcNow > _confidenceCacheExpiry)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var thresholds = await db.Cameras.AsNoTracking()
                        .Select(c => new { c.Id, c.ConfidenceThreshold })
                        .ToListAsync();
                    _confidenceCache.Clear();
                    foreach (var t in thresholds)
                        _confidenceCache[t.Id] = t.ConfidenceThreshold;
                    _confidenceCacheExpiry = DateTime.UtcNow.AddSeconds(60);
                }
            }
            finally { _confidenceCacheLock.Release(); }
        }
        return _confidenceCache.TryGetValue(cameraId, out var threshold) ? threshold : 0.45m;
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Camera {Id}: failed to update status.", cameraId);
        }
    }

    public void Dispose()
    {
        foreach (var (_, state) in _streams)
            try { state.Cts.Cancel(); state.Cts.Dispose(); } catch { /* ignore */ }
        _streams.Clear();
    }

    // Per-camera AI semaphore — ensures only 1 AI call in-flight per camera at a time.
    // If Python is still processing the previous frame, the new one is simply dropped.
    // This is the key guarantee that video NEVER lags regardless of AI speed.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _aiSemaphores = new();

    // Incident deduplication — don't create a new incident for the same camera+label+class
    // within a 60-second window. This prevents DB flooding when a detection persists
    // (e.g. a knife stays in frame for 30 seconds → only 1 incident, not 30).
    private readonly ConcurrentDictionary<string, DateTime> _lastIncidentTime = new();
    private static readonly TimeSpan IncidentCooldown = TimeSpan.FromSeconds(60);

    private record StreamState(CancellationTokenSource Cts);

    // ── AI response models — explicit JsonPropertyName to match Python snake_case ──
    private class AiDetectResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("camera_id")]
        public string CameraId { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("detections")]
        public List<AiDetection> Detections { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("inference_ms")]
        public float InferenceMs { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("face_recognition_ms")]
        public float FaceRecognitionMs { get; set; }
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
        [System.Text.Json.Serialization.JsonPropertyName("face_matched")]
        public bool FaceMatched { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("face_username")]
        public string? FaceUsername { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("face_confidence")]
        public float FaceConfidence { get; set; }
    }

    private class AiBoundingBox
    {
        [System.Text.Json.Serialization.JsonPropertyName("x")]
        public int X { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("y")]
        public int Y { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("w")]
        public int W { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("h")]
        public int H { get; set; }
    }
}
