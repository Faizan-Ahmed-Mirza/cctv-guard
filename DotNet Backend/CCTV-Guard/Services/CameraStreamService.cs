using System.Collections.Concurrent;
using System.Diagnostics;
using CCTV_Guard.Data;
using CCTV_Guard.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace CCTV_Guard.Services;

/// <summary>
/// Per-camera FFmpeg process that:
///   1. Reads RTSP/RTMP stream
///   2. Outputs JPEG frames to stdout pipe
///   3. Pushes each frame as base64 via SignalR to Angular
///
/// Angular receives frames → draws on <canvas> → smooth live video ~15-25 FPS
/// Same frames are also forwarded to Python AI service for detection.
/// </summary>
public class CameraStreamService : IDisposable
{
    // cameraId → running stream state
    private readonly ConcurrentDictionary<string, StreamState> _streams = new();

    private readonly IHubContext<CameraStreamHub> _hub;
    private readonly ILogger<CameraStreamService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private static string? _ffmpegExePath;
    private static bool _ffmpegReady = false;
    private static readonly SemaphoreSlim _ffmpegInitLock = new(1, 1);

    // Exposed so CamerasController can use the same binary for MJPEG endpoint
    public string? FfmpegExePath => _ffmpegExePath;

    public CameraStreamService(
        IHubContext<CameraStreamHub> hub,
        ILogger<CameraStreamService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _hub          = hub;
        _logger       = logger;
        _scopeFactory = scopeFactory;
    }

    // ── FFmpeg bootstrap ──────────────────────────────────────────────────────

    public async Task EnsureFfmpegAsync()
    {
        if (_ffmpegReady) return;
        await _ffmpegInitLock.WaitAsync();
        try
        {
            if (_ffmpegReady) return;

            var ffmpegDir = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg-bin");
            Directory.CreateDirectory(ffmpegDir);

            var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
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
        finally
        {
            _ffmpegInitLock.Release();
        }
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

        var cts   = new CancellationTokenSource();
        var state = new StreamState(cts);
        _streams[cameraId] = state;

        // Run in background — don't await
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
            _logger.LogInformation("Camera {Id}: stream stopped.", cameraId);
        }

        await UpdateCameraStatusAsync(cameraId, "offline");

        // Notify Angular that this camera went offline
        await _hub.Clients.Group($"camera-{cameraId}")
            .SendAsync("CameraOffline", new { cameraId });
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
                process = StartFfmpegProcess(cameraId, streamUrl);
                await ReadFramesAsync(cameraId, process, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Camera {Id}: stream error, retrying in 3s...", cameraId);
                await UpdateCameraStatusAsync(cameraId, "error");
                try { await Task.Delay(3000, ct); } catch { break; }
                await UpdateCameraStatusAsync(cameraId, "online");
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

    private Process StartFfmpegProcess(string cameraId, string streamUrl)
    {
        var isRtmp = streamUrl.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase);
        var inputArgs = isRtmp
            ? $"-fflags nobuffer -flags low_delay -i \"{streamUrl}\""
            : $"-fflags nobuffer -flags low_delay -rtsp_transport tcp -i \"{streamUrl}\"";

        var args = string.Join(" ",
            "-loglevel warning",
            inputArgs,
            "-vf \"fps=15,scale=854:480\"",
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
            RedirectStandardOutput = true,   // read JPEG frames from stdout
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        // Set large buffer for stdout to avoid blocking FFmpeg
        var process = new Process { StartInfo = psi };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger.LogDebug("FFmpeg [{Id}]: {Line}", cameraId, e.Data);
        };

        process.Start();
        process.BeginErrorReadLine();

        _logger.LogInformation("Camera {Id}: FFmpeg started (PID {Pid})", cameraId, process.Id);
        return process;
    }

    private async Task ReadFramesAsync(string cameraId, Process process, CancellationToken ct)
    {
        var stream   = process.StandardOutput.BaseStream;
        var buf      = new byte[131072];
        var accumulator = new List<byte>(512 * 1024);
        var frameNum = 0;

        while (!ct.IsCancellationRequested)
        {
            int bytesRead;
            try { bytesRead = await stream.ReadAsync(buf, 0, buf.Length, ct); }
            catch (OperationCanceledException) { break; }
            if (bytesRead == 0) break;

            // Append new bytes to accumulator
            for (int i = 0; i < bytesRead; i++)
                accumulator.Add(buf[i]);

            // Extract all complete JPEG frames from accumulator
            while (true)
            {
                // Find SOI (FF D8)
                int soiIdx = -1;
                for (int i = 0; i < accumulator.Count - 1; i++)
                {
                    if (accumulator[i] == 0xFF && accumulator[i + 1] == 0xD8)
                    { soiIdx = i; break; }
                }
                if (soiIdx < 0) break; // no start marker yet

                // Find EOI (FF D9) after SOI
                int eoiIdx = -1;
                for (int i = soiIdx + 2; i < accumulator.Count - 1; i++)
                {
                    if (accumulator[i] == 0xFF && accumulator[i + 1] == 0xD9)
                    { eoiIdx = i + 1; break; } // eoiIdx points to D9
                }
                if (eoiIdx < 0) break; // frame not complete yet — wait for more data

                // Extract complete JPEG: soiIdx to eoiIdx inclusive
                int frameLen = eoiIdx - soiIdx + 1;
                var jpeg = new byte[frameLen];
                accumulator.CopyTo(soiIdx, jpeg, 0, frameLen);

                // Remove everything up to and including this frame
                accumulator.RemoveRange(0, eoiIdx + 1);

                // Skip tiny frames (< 5 KB = likely corrupt)
                if (frameLen < 5000) continue;

                frameNum++;
                var base64 = Convert.ToBase64String(jpeg);
                var fn     = frameNum;

                if (fn <= 3 || fn % 100 == 0)
                    _logger.LogInformation("Camera {Id}: sending frame #{Num}, size={Size} bytes", cameraId, fn, frameLen);

                try
                {
                    await _hub.Clients.Group($"camera-{cameraId}")
                        .SendAsync("ReceiveFrame", new
                        {
                            cameraId,
                            frame    = base64,
                            frameNum = fn,
                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        }, ct);
                }
                catch (OperationCanceledException) { return; }
                catch { /* client disconnected */ }
            }

            // Safety: if accumulator grows > 5 MB without a complete frame, reset
            if (accumulator.Count > 5 * 1024 * 1024)
            {
                _logger.LogWarning("Camera {Id}: accumulator overflow, resetting", cameraId);
                accumulator.Clear();
            }
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
            var cam = await db.Cameras.FindAsync(cameraId);
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
}
