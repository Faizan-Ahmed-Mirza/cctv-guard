using System.Security.Claims;
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

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);
}
