using System.Collections.Concurrent;
using System.Diagnostics;
using CCTV_Guard.Data;
using Microsoft.EntityFrameworkCore;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace CCTV_Guard.Services;

/// <summary>
/// Manages per-camera FFmpeg processes that transcode RTSP → HLS segments.
/// FFmpeg binaries are auto-downloaded on first use — no manual install needed.
/// </summary>
public class HlsStreamingService : IDisposable
{
    private readonly ConcurrentDictionary<string, Process> _processes = new();
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<HlsStreamingService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private static string? _ffmpegExePath;
    private static bool _ffmpegReady = false;
    private static readonly SemaphoreSlim _ffmpegInitLock = new(1, 1);

    public HlsStreamingService(
        IWebHostEnvironment env,
        ILogger<HlsStreamingService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _env          = env;
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
                _logger.LogInformation("FFmpeg not found — downloading automatically (one-time ~70 MB)...");
                FFmpeg.SetExecutablesPath(ffmpegDir);
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegDir);
                _logger.LogInformation("FFmpeg downloaded to {Dir}", ffmpegDir);
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
        // Already running
        if (_processes.TryGetValue(cameraId, out var existing) && !existing.HasExited)
            return;

        var rtspUrl = await GetRtspUrlAsync(cameraId);
        if (string.IsNullOrWhiteSpace(rtspUrl))
        {
            _logger.LogWarning("Camera {Id}: no RTSP URL configured.", cameraId);
            return;
        }

        await EnsureFfmpegAsync();

        var outputDir = GetHlsDir(cameraId);
        Directory.CreateDirectory(outputDir);
        var playlistPath = Path.Combine(outputDir, "index.m3u8");

        // Delete stale playlist so HLS.js doesn't load old segments
        if (File.Exists(playlistPath)) File.Delete(playlistPath);

        // Build args — rtsp_transport only applies to RTSP, not RTMP
        var isRtmp = rtspUrl.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase);
        var inputArgs = isRtmp
            ? $"-i \"{rtspUrl}\""
            : $"-rtsp_transport tcp -timeout 10000000 -i \"{rtspUrl}\"";

        var args = string.Join(" ",
            "-loglevel info",
            inputArgs,
            "-c:v libx264",                 // re-encode to H.264 (compatible with HLS)
            "-preset ultrafast",            // fastest encoding, lowest CPU delay
            "-tune zerolatency",            // minimize latency
            "-an",                          // no audio
            "-hls_time 2",
            "-hls_list_size 5",
            "-hls_flags delete_segments+append_list+omit_endlist",
            "-f hls",
            $"\"{playlistPath}\""
        );

        var psi = new ProcessStartInfo
        {
            FileName               = _ffmpegExePath!,
            Arguments              = args,
            UseShellExecute        = false,
            RedirectStandardError  = true,
            RedirectStandardOutput = false,
            CreateNoWindow         = true,
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        // Log FFmpeg stderr so we can see errors
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger.LogInformation("FFmpeg [{Id}]: {Line}", cameraId, e.Data);
        };

        process.Exited += (_, _) =>
        {
            _processes.TryRemove(cameraId, out _);
            _logger.LogWarning("Camera {Id}: FFmpeg process exited (code {Code}).",
                cameraId, process.ExitCode);
            _ = UpdateCameraStatusAsync(cameraId, "error");
        };

        try
        {
            process.Start();
            process.BeginErrorReadLine();
            _processes[cameraId] = process;
            _logger.LogInformation("Camera {Id}: FFmpeg started (PID {Pid}). Args: {Args}",
                cameraId, process.Id, args);
            await UpdateCameraStatusAsync(cameraId, "online");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Camera {Id}: failed to start FFmpeg.", cameraId);
            await UpdateCameraStatusAsync(cameraId, "error");
            throw;
        }
    }

    public async Task StopAsync(string cameraId)
    {
        if (_processes.TryRemove(cameraId, out var process))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                process.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Camera {Id}: error stopping FFmpeg.", cameraId);
            }
        }

        await UpdateCameraStatusAsync(cameraId, "offline");

        var dir = GetHlsDir(cameraId);
        if (Directory.Exists(dir))
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
    }

    public bool IsStreaming(string cameraId) =>
        _processes.TryGetValue(cameraId, out var p) && !p.HasExited;

    public string GetPlaylistUrl(string cameraId) =>
        $"/hls/{cameraId}/index.m3u8";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetHlsDir(string cameraId)
    {
        // Write HLS segments to hls-output/ (outside wwwroot to avoid MSBuild conflicts)
        var hlsRoot = Path.Combine(Directory.GetCurrentDirectory(), "hls-output");
        return Path.Combine(hlsRoot, cameraId);
    }

    private async Task<string?> GetRtspUrlAsync(string cameraId)
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
        foreach (var (_, p) in _processes)
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); p.Dispose(); }
            catch { /* ignore */ }
        _processes.Clear();
    }
}
