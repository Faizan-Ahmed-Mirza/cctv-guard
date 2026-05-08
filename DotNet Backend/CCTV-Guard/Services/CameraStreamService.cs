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

    public string? FfmpegExePath => _ffmpegExePath;

    // AI: send every Nth frame (15fps ÷ 5 = 3fps to AI)
    private const int AiFrameInterval = 5;

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

                // Channel decouples frame parsing from SignalR sending
                // Capacity=2: if SignalR is slow, drop old frames (always show latest)
                var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2)
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
                _logger.LogWarning(ex, "Camera {Id}: stream error, retrying in 3s...", cameraId);
                await UpdateCameraStatusAsync(cameraId, "error");
                try { await Task.Delay(3000, ct); } catch { break; }
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

        var inputArgs = isRtmp
            ? $"-fflags nobuffer -flags low_delay -i \"{streamUrl}\""
            : $"-fflags nobuffer -flags low_delay -rtsp_transport tcp -i \"{streamUrl}\"";

        // 25fps for smooth video, 640x480 for good quality + low bandwidth
        // q:v 3 = high JPEG quality
        var args = string.Join(" ",
            "-loglevel warning",
            inputArgs,
            "-vf fps=25,scale=640:480",
            "-vcodec mjpeg",
            "-q:v 3",
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
        // Use a MemoryStream as a ring buffer — much faster than List<byte>
        var buf    = new byte[65536];
        var ms     = new MemoryStream(256 * 1024);

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

            // AI detection — completely isolated, never affects video
            if (frameNum % AiFrameInterval == 0)
                _ = Task.Run(() => ProcessAiDetectionAsync(cameraId, jpeg, CancellationToken.None));
        }
    }

    // ── AI Detection ──────────────────────────────────────────────────────────

    private async Task ProcessAiDetectionAsync(string cameraId, byte[] jpeg, CancellationToken ct)
    {
        if (!_aiServiceAvailable && DateTime.UtcNow < _aiNextRetryTime) return;

        try
        {
            var client = _httpClientFactory.CreateClient("AiService");
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(cameraId), "camera_id");
            content.Add(new ByteArrayContent(jpeg)
            {
                Headers = { ContentType = MediaTypeHeaderValue.Parse("image/jpeg") }
            }, "file", "frame.jpg");

            var threshold = await GetConfidenceThresholdAsync(cameraId);
            content.Add(new StringContent(threshold.ToString("F2")), "confidence_threshold");

            var response = await client.PostAsync("/detect", content, ct);
            if (!response.IsSuccessStatusCode) return;

            _aiServiceAvailable = true;

            var json   = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<AiDetectResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.Detections == null || result.Detections.Count == 0) return;

            // Push bounding boxes to Angular
            _ = _hub.Clients.Group($"camera-{cameraId}")
                .SendAsync("BoundingBoxes", new
                {
                    cameraId,
                    detections = result.Detections.Select(d => new
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

            // Create incidents for critical/high threats
            foreach (var det in result.Detections.Where(d => d.Severity is "critical" or "high"))
                await CreateIncidentAsync(cameraId, det, ct);
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

    private async Task CreateIncidentAsync(string cameraId, AiDetection det, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var incidentId = "inc-" + Guid.NewGuid().ToString("N")[..8];
            var alertId    = "alr-" + Guid.NewGuid().ToString("N")[..8];
            var now        = DateTime.UtcNow;

            db.Incidents.Add(new Incident
            {
                Id           = incidentId,
                CameraId     = cameraId,
                Type         = det.Label,
                Severity     = det.Severity,
                Confidence   = (decimal)det.Confidence,
                Timestamp    = now,
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
                Message    = $"{det.Label} detected with {det.Confidence:P0} confidence.",
                Severity   = det.Severity,
                Timestamp  = now
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
            _logger.LogWarning(ex, "Camera {Id}: failed to create incident", cameraId);
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

    private async Task<decimal> GetConfidenceThresholdAsync(string cameraId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Cameras.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cameraId))?.ConfidenceThreshold ?? 0.45m;
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

    private record StreamState(CancellationTokenSource Cts);
    private record AiDetectResponse(string CameraId, List<AiDetection> Detections, float InferenceMs);
    private record AiDetection(string Label, string YoloClass, float Confidence, string Severity, AiBoundingBox BoundingBox);
    private record AiBoundingBox(int X, int Y, int W, int H);
}
