using CCTV_Guard.Hubs;
using CCTV_Guard.Models.DTOs.Alert;
using Microsoft.AspNetCore.SignalR;

namespace CCTV_Guard.Services;

/// <summary>
/// Wraps IHubContext so any service/controller can push real-time events
/// to connected Angular clients without depending on SignalR directly.
/// </summary>
public class HubNotificationService
{
    private readonly IHubContext<AlertsHub> _hub;

    public HubNotificationService(IHubContext<AlertsHub> hub) => _hub = hub;

    /// <summary>Broadcast a new alert to ALL connected clients.</summary>
    public Task SendNewAlertAsync(AlertDto alert) =>
        _hub.Clients.All.SendAsync("NewAlert", alert);

    /// <summary>Broadcast a new alert from a raw Alert entity (used by CameraStreamService).</summary>
    public Task SendNewAlertAsync(Models.Entities.Alert alert, string cameraName) =>
        _hub.Clients.All.SendAsync("NewAlert", new AlertDto
        {
            Id         = alert.Id,
            IncidentId = alert.IncidentId,
            Type       = alert.Type,
            Message    = alert.Message,
            CameraName = cameraName,
            Severity   = alert.Severity,
            Timestamp  = new DateTimeOffset(DateTime.SpecifyKind(alert.Timestamp, DateTimeKind.Utc)),
            Read       = false,
            Dismissed  = false
        });

    /// <summary>Broadcast an incident status change to ALL connected clients.</summary>
    public Task SendIncidentUpdatedAsync(string incidentId, string status) =>
        _hub.Clients.All.SendAsync("IncidentUpdated", new { id = incidentId, status });

    /// <summary>Broadcast a camera status change to ALL connected clients.</summary>
    public Task SendCameraStatusChangedAsync(string cameraId, string status) =>
        _hub.Clients.All.SendAsync("CameraStatusChanged", new { id = cameraId, status });

    /// <summary>Broadcast an alert dismissal to ALL connected clients.</summary>
    public Task SendAlertDismissedAsync(string alertId, string userId) =>
        _hub.Clients.All.SendAsync("AlertDismissed", new { alertId, userId });
}
