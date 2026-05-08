using System.Collections.Concurrent;
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

    // Tracks which cameras each connection is watching
    // connectionId → list of cameraIds
    private static readonly ConcurrentDictionary<string, HashSet<string>> _connectionCameras = new();

    // Tracks viewer count per camera so we can stop FFmpeg when nobody is watching
    private static readonly ConcurrentDictionary<string, int> _cameraViewers = new();

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

        // Track this connection → camera mapping, and only increment viewer count once per connection
        var alreadyJoined = false;
        _connectionCameras.AddOrUpdate(
            Context.ConnectionId,
            _ => new HashSet<string> { cameraId },
            (_, set) =>
            {
                lock (set)
                {
                    alreadyJoined = !set.Add(cameraId); // Add returns false if already present
                }
                return set;
            });

        if (!alreadyJoined)
            _cameraViewers.AddOrUpdate(cameraId, 1, (_, count) => count + 1);

        // Start stream if not already running
        if (!_streamService.IsStreaming(cameraId))
            await _streamService.StartAsync(cameraId);
    }

    /// <summary>
    /// Called by Angular when it closes a camera feed or navigates away.
    /// Removes from group. Stops FFmpeg if no more viewers.
    /// </summary>
    public async Task LeaveCamera(string cameraId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"camera-{cameraId}");
        await DecrementViewerAsync(cameraId);

        if (_connectionCameras.TryGetValue(Context.ConnectionId, out var cameras))
            lock (cameras) { cameras.Remove(cameraId); }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Clean up all cameras this connection was watching
        if (_connectionCameras.TryRemove(Context.ConnectionId, out var cameras))
        {
            string[] snapshot;
            lock (cameras) { snapshot = cameras.ToArray(); }
            foreach (var cameraId in snapshot)
                await DecrementViewerAsync(cameraId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task DecrementViewerAsync(string cameraId)
    {
        var newCount = _cameraViewers.AddOrUpdate(cameraId,
            0,
            (_, count) => Math.Max(0, count - 1));

        // Stop FFmpeg when nobody is watching — saves CPU and bandwidth
        if (newCount == 0)
        {
            _cameraViewers.TryRemove(cameraId, out _);
            if (_streamService.IsStreaming(cameraId))
                await _streamService.StopAsync(cameraId);
        }
    }
}
