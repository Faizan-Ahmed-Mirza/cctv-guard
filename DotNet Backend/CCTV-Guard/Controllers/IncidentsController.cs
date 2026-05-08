using System.Security.Claims;
using CCTV_Guard.Models.DTOs.Incident;
using CCTV_Guard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCTV_Guard.Controllers;

[ApiController]
[Route("api/incidents")]
[Authorize]
public class IncidentsController : ControllerBase
{
    private readonly IncidentService _incidentService;
    private readonly HubNotificationService _hub;

    public IncidentsController(IncidentService incidentService, HubNotificationService hub)
    {
        _incidentService = incidentService;
        _hub = hub;
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

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")!);
}
