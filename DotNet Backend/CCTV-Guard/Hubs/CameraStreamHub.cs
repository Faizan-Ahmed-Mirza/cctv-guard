using CCTV_Guard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CCTV_Guard.Hubs;

/// <summary>
/// SignalR hub for live camera video frames.
///
/// Angular connects and calls JoinCamera(cameraId) to subscribe to a camera's frames.
/// Server pushes:
///   - ReceiveFrame  : { cameraId, frame (base64 JPEG), frameNum, timestamp }
///   - CameraOffline : { cameraId }
///
/// Separate from AlertsHub to keep video traffic isolated from alert traffic.
/// </summary>
[Authorize]
public class CameraStreamHub : Hub
{
    private readonly CameraStreamService _streamService;

    public CameraStreamHub(CameraStreamService streamService)
    {
        _streamService = streamService;
    }

    /// <summary>
    /// Called by Angular when it opens a camera feed.
    /// Adds the connection to the camera's SignalR group and starts
    /// FFmpeg if not already running.
    /// </summary>
    public async Task JoinCamera(string cameraId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"camera-{cameraId}");

        // Start stream if not already running
        if (!_streamService.IsStreaming(cameraId))
            await _streamService.StartAsync(cameraId);
    }

    /// <summary>
    /// Called by Angular when it closes a camera feed or navigates away.
    /// Removes from group. FFmpeg keeps running if other clients are still watching.
    /// </summary>
    public async Task LeaveCamera(string cameraId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"camera-{cameraId}");
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Groups are cleaned up automatically by SignalR on disconnect
        await base.OnDisconnectedAsync(exception);
    }
}
