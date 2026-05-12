using System.Security.Claims;
using CCTV_Guard.Models.DTOs.Alert;
using CCTV_Guard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCTV_Guard.Controllers;

[ApiController]
[Route("api/alerts")]
[Authorize]
public class AlertsController : ControllerBase
{
    private readonly AlertService _alertService;
    private readonly HubNotificationService _hub;

    public AlertsController(AlertService alertService, HubNotificationService hub)
    {
        _alertService = alertService;
        _hub = hub;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? severity,
        [FromQuery] bool dismissed = false)
    {
        var userId = GetUserId();
        return Ok(await _alertService.GetAllAsync(userId, severity, dismissed));
    }

    [HttpPatch("{id}/read")]
    public async Task<IActionResult> MarkRead(string id)
    {
        await _alertService.MarkReadAsync(id, GetUserId());
        return NoContent();
    }

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await _alertService.MarkAllReadAsync(GetUserId());
        return NoContent();
    }

    [HttpPatch("{id}/dismiss")]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> Dismiss(string id)
    {
        var userId = GetUserId();
        await _alertService.DismissAsync(id, userId);

        // Push real-time dismissal to all connected clients
        await _hub.SendAlertDismissedAsync(id, userId.ToString());
        return NoContent();
    }

    /// <summary>
    /// Escalate an alert to emergency services.
    /// Broadcasts ReceiveEmergencyNotification to all SignalR clients (Angular + Flutter).
    /// </summary>
    [HttpPatch("{id}/escalate")]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> Escalate(string id)
    {
        var userId   = GetUserId();
        var username = User.FindFirstValue(ClaimTypes.Name)
                    ?? User.FindFirstValue("name")
                    ?? "Operator";

        var alert = await _alertService.EscalateAsync(id, userId);
        if (alert == null) return NotFound();

        var now = DateTimeOffset.UtcNow;
        var payload = new EmergencyNotificationDto
        {
            AlertId      = alert.Id,
            IncidentId   = alert.IncidentId,
            Type         = alert.Type,
            Message      = alert.Message,
            CameraName   = alert.Camera?.Name ?? string.Empty,
            Severity     = alert.Severity,
            Timestamp    = new DateTimeOffset(DateTime.SpecifyKind(alert.Timestamp, DateTimeKind.Utc)),
            ImageUrl     = alert.Incident?.ThumbnailUrl,
            EscalatedBy  = username,
            EscalatedAt  = now,
        };

        // Broadcast to ALL clients — Angular updates the card, Flutter pushes to Notifications tab
        await _hub.SendEmergencyNotificationAsync(payload);

        return Ok(payload);
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);
}
