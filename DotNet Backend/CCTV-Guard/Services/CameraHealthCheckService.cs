using CCTV_Guard.Data;
using CCTV_Guard.Services;
using Microsoft.EntityFrameworkCore;

namespace CCTV_Guard.Services;

/// <summary>
/// Background service that determines camera online/offline status.
///
/// Priority order:
///   1. If FFmpeg is actively streaming → camera is ONLINE (most reliable)
///   2. If not streaming → probe via HTTP or TCP to detect if reachable
///
/// This prevents the "blinking" issue where the camera is reachable via RTSP
/// but not via TCP/HTTP from a different subnet.
/// </summary>
public class CameraHealthCheckService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HubNotificationService _hub;
    private readonly CameraStreamService _streamService;
    private readonly ILogger<CameraHealthCheckService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    // Check every 2s — fast detection of offline cameras
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProbeTimeout  = TimeSpan.FromSeconds(2);

    public CameraHealthCheckService(
        IServiceScopeFactory scopeFactory,
        HubNotificationService hub,
        CameraStreamService streamService,
        ILogger<CameraHealthCheckService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _scopeFactory   = scopeFactory;
        _hub            = hub;
        _streamService  = streamService;
        _logger         = logger;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CameraHealthCheckService started (interval: {Interval}s)", ProbeInterval.TotalSeconds);

        // First probe immediately on startup
        try { await CheckAllCamerasAsync(stoppingToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { _logger.LogWarning(ex, "Startup probe failed"); }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckAllCamerasAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { _logger.LogWarning(ex, "Health check cycle failed"); }

            try { await Task.Delay(ProbeInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("CameraHealthCheckService stopped.");
    }

    private async Task CheckAllCamerasAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cameras = await db.Cameras
            .AsNoTracking()
            .Select(c => new { c.Id, c.IpAddress, c.Port, c.RtspUrl, c.Status })
            .ToListAsync(ct);

        if (cameras.Count == 0) return;

        var sem = new SemaphoreSlim(10);
        var tasks = cameras.Select(async cam =>
        {
            await sem.WaitAsync(ct);
            try
            {
                bool isOnline;

                // ── Priority 1: FFmpeg is streaming → definitely online ────────
                if (_streamService.IsStreaming(cam.Id))
                {
                    isOnline = true;
                }
                else
                {
                    // ── Priority 2: Probe the camera directly ─────────────────
                    isOnline = await ProbeCameraAsync(cam.IpAddress, cam.Port, cam.RtspUrl, ct);
                }

                var newStatus = isOnline ? "online" : "offline";

                if (cam.Status != newStatus)
                {
                    await UpdateStatusAsync(cam.Id, newStatus, ct);
                    _logger.LogInformation("Camera {Id}: {Old} → {New}", cam.Id, cam.Status, newStatus);
                }
            }
            finally { sem.Release(); }
        });

        await Task.WhenAll(tasks);
    }

    private async Task<bool> ProbeCameraAsync(string ipAddress, int port, string? rtspUrl, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ProbeTimeout);

            // HTTP-based cameras (IP Webcam app)
            if (!string.IsNullOrWhiteSpace(rtspUrl) &&
                rtspUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                var uri      = new Uri(rtspUrl);
                var probeUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}/shot.jpg";
                var client   = _httpClientFactory.CreateClient("HealthCheck");
                using var response = await client.GetAsync(probeUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                return response.IsSuccessStatusCode;
            }

            // RTSP/RTMP — TCP connect
            using var tcp = new System.Net.Sockets.TcpClient();
            await tcp.ConnectAsync(ipAddress, port, cts.Token);
            return tcp.Connected;
        }
        catch { return false; }
    }

    private async Task UpdateStatusAsync(string cameraId, string newStatus, CancellationToken ct)
    {
        try
        {
            // Push SignalR IMMEDIATELY — don't wait for DB write
            // This makes the UI update in real-time without waiting for SQL
            _ = _hub.SendCameraStatusChangedAsync(cameraId, newStatus);

            // DB write in background — non-blocking
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cam = await db.Cameras.FirstOrDefaultAsync(c => c.Id == cameraId, ct);
            if (cam == null) return;
            cam.Status   = newStatus;
            cam.LastSeen = newStatus == "online" ? DateTime.UtcNow : cam.LastSeen;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update status for camera {Id}", cameraId);
        }
    }
}
