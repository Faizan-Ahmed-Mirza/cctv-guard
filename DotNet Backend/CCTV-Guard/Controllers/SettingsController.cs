using CCTV_Guard.Data;
using CCTV_Guard.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCTV_Guard.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize(Roles = "Admin")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;

    public SettingsController(AppDbContext db) => _db = db;

    [HttpGet("ai")]
    public async Task<IActionResult> GetAi()
    {
        var settings = await _db.AiSettings.FindAsync(1);
        if (settings == null)
        {
            // Auto-create defaults if the row was somehow deleted
            settings = new AiSettings { Id = 1 };
            _db.AiSettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        return Ok(settings);
    }

    [HttpPut("ai")]
    public async Task<IActionResult> UpdateAi([FromBody] AiSettings req)
    {
        var settings = await _db.AiSettings.FindAsync(1);
        if (settings == null) return NotFound();

        settings.FightDetection = req.FightDetection;
        settings.WeaponDetection = req.WeaponDetection;
        settings.IntrusionDetection = req.IntrusionDetection;
        settings.FaceRecognition = req.FaceRecognition;
        settings.LicensePlate = req.LicensePlate;
        settings.GlobalConfidence = req.GlobalConfidence;
        settings.AlertLatencyTarget = req.AlertLatencyTarget;
        settings.FrameProcessingRate = req.FrameProcessingRate;
        settings.GpuAcceleration = req.GpuAcceleration;
        settings.ModelVersion = req.ModelVersion;
        settings.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(settings);
    }
}
