using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CCTV_Guard.Hubs;

/// <summary>
/// Real-time hub for pushing alerts, incident updates, and camera status changes
/// to connected Angular clients.
///
/// Connect from Angular:
///   const connection = new HubConnectionBuilder()
///     .withUrl('/hubs/alerts', { accessTokenFactory: () => token })
///     .build();
///
/// Events the server pushes to clients:
///   - NewAlert          : AlertDto
///   - IncidentUpdated   : { id, status }
///   - CameraStatusChanged: { id, status }
///   - AlertDismissed    : { alertId, userId }
/// </summary>
[Authorize]
public class AlertsHub : Hub
{
    /// <summary>
    /// Called when a client connects. Adds them to a role-based group
    /// so we can target broadcasts by role if needed.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirst("role")?.Value
                ?? Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        if (!string.IsNullOrEmpty(role))
            await Groups.AddToGroupAsync(Context.ConnectionId, role);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var role = Context.User?.FindFirst("role")?.Value
                ?? Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        if (!string.IsNullOrEmpty(role))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, role);

        await base.OnDisconnectedAsync(exception);
    }
}
