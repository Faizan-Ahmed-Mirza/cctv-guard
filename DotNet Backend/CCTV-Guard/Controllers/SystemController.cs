using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCTV_Guard.Controllers;

[ApiController]
[Route("api/system")]
[Authorize(Roles = "Admin")]
public class SystemController : ControllerBase
{
    [HttpGet("info")]
    public IActionResult GetInfo() => Ok(new
    {
        uptime = "99.7%",
        avgLatency = 1.8,
        detectionAccuracy = 96.2,
        frameLatencyMs = 12,
        cloudProvider = "AWS / GCP",
        database = "SQL Server",
        backendVersion = "1.0.0"
    });
}
