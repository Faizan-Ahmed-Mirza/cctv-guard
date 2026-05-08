using CCTV_Guard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCTV_Guard.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize(Roles = "Admin")]
public class AnalyticsController : ControllerBase
{
    private readonly AnalyticsService _analyticsService;

    public AnalyticsController(AnalyticsService analyticsService) => _analyticsService = analyticsService;

    [HttpGet("user-sessions")]
    public async Task<IActionResult> GetUserSessions(
        [FromQuery] int year,
        [FromQuery] int? month,
        [FromQuery] string? role) =>
        Ok(await _analyticsService.GetUserSessionsAsync(year, month, role));

    [HttpGet("camera-detections")]
    public async Task<IActionResult> GetCameraDetections() =>
        Ok(await _analyticsService.GetCameraDetectionsAsync());

    [HttpGet("monthly-alerts")]
    public async Task<IActionResult> GetMonthlyAlerts([FromQuery] int year) =>
        Ok(await _analyticsService.GetMonthlyAlertsAsync(year));

    [HttpGet("camera-alerts")]
    public async Task<IActionResult> GetCameraAlerts() =>
        Ok(await _analyticsService.GetCameraAlertsAsync());

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview([FromQuery] int year) =>
        Ok(await _analyticsService.GetOverviewAsync(year));
}
