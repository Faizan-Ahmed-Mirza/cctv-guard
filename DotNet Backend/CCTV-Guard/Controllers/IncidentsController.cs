using System.Security.Claims;
using CCTV_Guard.Data;
using CCTV_Guard.Models.DTOs.Alert;
using CCTV_Guard.Models.DTOs.Incident;
using CCTV_Guard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCTV_Guard.Controllers;

[ApiController]
[Route("api/incidents")]
[Authorize]
public class IncidentsController : ControllerBase
{
    private readonly IncidentService _incidentService;
    private readonly HubNotificationService _hub;
    private readonly AlertService _alertService;
    private readonly AppDbContext _db;

    public IncidentsController(
        IncidentService incidentService,
        HubNotificationService hub,
        AlertService alertService,
        AppDbContext db)
    {
        _incidentService = incidentService;
        _hub             = hub;
        _alertService    = alertService;
        _db              = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] IncidentFilterParams p) =>
        Ok(await _incidentService.GetAllAsync(p));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var inc = await _incidentService.GetByIdAsync(id);
        return inc == null ? NotFound() : Ok(inc);
    }

    [HttpPatch("{id}/acknowledge")]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> Acknowledge(string id)
    {
        var userId = GetUserId();
        var inc = await _incidentService.AcknowledgeAsync(id, userId);
        if (inc == null) return NotFound();

        // Push real-time update to all connected clients
        await _hub.SendIncidentUpdatedAsync(id, "acknowledged");
        return Ok(inc);
    }

    [HttpPatch("{id}/resolve")]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> Resolve(string id, [FromBody] ResolveIncidentRequest? req)
    {
        var userId = GetUserId();
        var inc = await _incidentService.ResolveAsync(id, userId, req?.Notes);
        if (inc == null) return NotFound();

        // Push real-time update to all connected clients
        await _hub.SendIncidentUpdatedAsync(id, "resolved");
        return Ok(inc);
    }

    /// <summary>
    /// Escalate an incident to emergency services.
    /// Finds the linked alert and broadcasts ReceiveEmergencyNotification to all clients.
    /// </summary>
    [HttpPatch("{id}/escalate")]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> Escalate(string id)
    {
        var userId   = GetUserId();
        var username = User.FindFirstValue(ClaimTypes.Name)
                    ?? User.FindFirstValue("name")
                    ?? "Operator";

        // Find the alert linked to this incident
        var alert = await _db.Alerts
            .Include(a => a.Camera)
            .Include(a => a.Incident)
            .FirstOrDefaultAsync(a => a.IncidentId == id);

        if (alert == null) return NotFound("No alert linked to this incident.");

        // Save escalation on the alert
        await _alertService.EscalateAsync(alert.Id, userId);

        var now = DateTimeOffset.UtcNow;
        var payload = new EmergencyNotificationDto
        {
            AlertId      = alert.Id,
            IncidentId   = id,
            Type         = alert.Type,
            Message      = alert.Message,
            CameraName   = alert.Camera?.Name ?? string.Empty,
            Severity     = alert.Severity,
            Timestamp    = new DateTimeOffset(DateTime.SpecifyKind(alert.Timestamp, DateTimeKind.Utc)),
            ImageUrl     = alert.Incident?.ThumbnailUrl,
            EscalatedBy  = username,
            EscalatedAt  = now,
        };

        await _hub.SendEmergencyNotificationAsync(payload);
        return Ok(payload);
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);
}
