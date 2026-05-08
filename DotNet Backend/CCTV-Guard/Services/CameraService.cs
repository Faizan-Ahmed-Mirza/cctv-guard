using CCTV_Guard.Data;
using CCTV_Guard.Models.DTOs.Camera;
using CCTV_Guard.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCTV_Guard.Services;

public class CameraService
{
    private readonly AppDbContext _db;
    private readonly CameraStreamService _streamService;

    public CameraService(AppDbContext db, CameraStreamService streamService)
    {
        _db            = db;
        _streamService = streamService;
    }

    public async Task<List<CameraDto>> GetAllAsync() =>
        await _db.Cameras
            .OrderBy(c => c.Name)
            .Select(c => ToDto(c))
            .ToListAsync();

    public async Task<CameraDto?> GetByIdAsync(string id)
    {
        var c = await _db.Cameras.FirstOrDefaultAsync(c => c.Id == id);
        return c == null ? null : ToDto(c);
    }

    public async Task<CameraDto> CreateAsync(CreateCameraRequest req)
    {
        var id = "cam-" + Guid.NewGuid().ToString("N")[..8];
        var camera = new Camera
        {
            Id                  = id,
            Name                = req.Name,
            Location            = req.Location,
            IpAddress           = req.IpAddress,
            Port                = req.Port,
            DetectionEnabled    = req.DetectionEnabled,
            ConfidenceThreshold = req.ConfidenceThreshold,
            FrameRate           = req.FrameRate,
            StreamUrl           = req.StreamUrl,
            RtspUrl             = req.RtspUrl,
            Status              = "offline"
        };
        _db.Cameras.Add(camera);
        await _db.SaveChangesAsync();
        return ToDto(camera);
    }

    public async Task<CameraDto?> UpdateAsync(string id, UpdateCameraRequest req)
    {
        // Use FirstOrDefaultAsync — FindAsync bypasses the global IsDeleted query filter
        var camera = await _db.Cameras.FirstOrDefaultAsync(c => c.Id == id);
        if (camera == null) return null;

        var rtspChanged = camera.RtspUrl != req.RtspUrl;

        camera.Name                = req.Name;
        camera.Location            = req.Location;
        camera.IpAddress           = req.IpAddress;
        camera.Port                = req.Port;
        camera.DetectionEnabled    = req.DetectionEnabled;
        camera.ConfidenceThreshold = req.ConfidenceThreshold;
        camera.FrameRate           = req.FrameRate;
        camera.StreamUrl           = req.StreamUrl;
        camera.RtspUrl             = req.RtspUrl;
        // Do NOT update Status from the request — status is managed by CameraStreamService only

        await _db.SaveChangesAsync();

        // If RTSP URL changed, restart stream so new URL takes effect immediately
        if (rtspChanged)
        {
            await _streamService.StopAsync(id);
            if (!string.IsNullOrWhiteSpace(req.RtspUrl))
                await _streamService.StartAsync(id);
        }

        return ToDto(camera);
    }

    public async Task<CameraDto?> PatchDetectionAsync(string id, bool detectionEnabled)
    {
        var camera = await _db.Cameras.FirstOrDefaultAsync(c => c.Id == id);
        if (camera == null) return null;
        camera.DetectionEnabled = detectionEnabled;
        await _db.SaveChangesAsync();
        return ToDto(camera);
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var camera = await _db.Cameras.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        if (camera == null) return false;

        // Stop stream if running
        await _streamService.StopAsync(id);

        // Soft delete — preserves all incident and alert history
        camera.IsDeleted = true;
        camera.Status    = "offline";
        await _db.SaveChangesAsync();
        return true;
    }

    private static CameraDto ToDto(Camera c) => new()
    {
        Id                  = c.Id,
        Name                = c.Name,
        Location            = c.Location,
        IpAddress           = c.IpAddress,
        Port                = c.Port,
        Status              = c.Status,
        StreamUrl           = c.StreamUrl,
        RtspUrl             = c.RtspUrl,
        DetectionEnabled    = c.DetectionEnabled,
        ConfidenceThreshold = c.ConfidenceThreshold,
        FrameRate           = c.FrameRate,
        LastSeen            = c.LastSeen
    };
}
