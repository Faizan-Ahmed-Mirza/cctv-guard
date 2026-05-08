using CCTV_Guard.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCTV_Guard.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;

    public DashboardController(AppDbContext db) => _db = db;

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var today = DateTime.UtcNow.Date;
        var totalCameras = await _db.Cameras.CountAsync();
        var onlineCameras = await _db.Cameras.CountAsync(c => c.Status == "online");
        var todayIncidents = await _db.Incidents.CountAsync(i => i.Timestamp >= today);

        // Active alerts = alerts not dismissed by anyone (simplified: total alerts today)
        var activeAlerts = await _db.Alerts.CountAsync(a => a.Timestamp >= today);

        return Ok(new
        {
            totalCameras,
            onlineCameras,
            todayIncidents,
            activeAlerts,
            systemUptime = "99.7%",
            avgLatency = 1.8,
            detectionAccuracy = 96.2
        });
    }
}
