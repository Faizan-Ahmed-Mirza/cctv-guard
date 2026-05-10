using CCTV_Guard.Data;
using CCTV_Guard.Models.DTOs.Incident;
using Microsoft.EntityFrameworkCore;

namespace CCTV_Guard.Services;

public class IncidentService
{
    private readonly AppDbContext _db;

    public IncidentService(AppDbContext db) => _db = db;

    public async Task<PagedResult<IncidentDto>> GetAllAsync(IncidentFilterParams p)
    {
        var query = _db.Incidents.Include(i => i.Camera).AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.Type))
            query = query.Where(i => i.Type == p.Type);
        if (!string.IsNullOrWhiteSpace(p.Severity))
            query = query.Where(i => i.Severity == p.Severity);
        if (!string.IsNullOrWhiteSpace(p.Status))
            query = query.Where(i => i.Status == p.Status);
        if (!string.IsNullOrWhiteSpace(p.CameraId))
            query = query.Where(i => i.CameraId == p.CameraId);
        if (!string.IsNullOrWhiteSpace(p.Search))
            query = query.Where(i => i.Type.Contains(p.Search) || i.Camera.Name.Contains(p.Search) || i.Id.Contains(p.Search));

        var total = await query.CountAsync();
        var data = await query
            .OrderByDescending(i => i.Timestamp)
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .Select(i => ToDto(i))
            .ToListAsync();

        return new PagedResult<IncidentDto> { Data = data, Total = total, Page = p.Page, PageSize = p.PageSize };
    }

    public async Task<IncidentDto?> GetByIdAsync(string id)
    {
        var i = await _db.Incidents.Include(x => x.Camera).FirstOrDefaultAsync(x => x.Id == id);
        return i == null ? null : ToDto(i);
    }

    public async Task<IncidentDto?> AcknowledgeAsync(string id, Guid userId)
    {
        var incident = await _db.Incidents.FindAsync(id);
        if (incident == null) return null;

        incident.Status = "acknowledged";
        incident.AcknowledgedBy = userId;
        incident.AcknowledgedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    public async Task<IncidentDto?> ResolveAsync(string id, Guid userId, string? notes)
    {
        var incident = await _db.Incidents.FindAsync(id);
        if (incident == null) return null;

        incident.Status = "resolved";
        incident.ResolvedBy = userId;
        incident.ResolvedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(notes))
            incident.Notes = notes;
        await _db.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    private static IncidentDto ToDto(Models.Entities.Incident i) => new()
    {
        Id = i.Id,
        CameraId = i.CameraId,
        CameraName = i.Camera?.Name ?? string.Empty,
        Type = i.Type,
        Severity = i.Severity,
        Confidence = i.Confidence,
        // Explicitly mark as UTC so JSON serialiser emits "+00:00" suffix.
        // Without this, DateTime without Kind serialises without timezone info
        // and Angular treats it as local time (5h behind for PKT UTC+5).
        Timestamp = new DateTimeOffset(DateTime.SpecifyKind(i.Timestamp, DateTimeKind.Utc)),
        ThumbnailUrl = i.ThumbnailUrl,
        BoundingBox = (i.BoundingBoxX.HasValue) ? new BoundingBoxDto
        {
            X = i.BoundingBoxX!.Value,
            Y = i.BoundingBoxY!.Value,
            Width = i.BoundingBoxW!.Value,
            Height = i.BoundingBoxH!.Value
        } : null,
        Status = i.Status,
        Notes = i.Notes,
        AcknowledgedBy = i.AcknowledgedBy,
        AcknowledgedAt = i.AcknowledgedAt.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(i.AcknowledgedAt.Value, DateTimeKind.Utc))
            : null,
        ResolvedBy = i.ResolvedBy,
        ResolvedAt = i.ResolvedAt.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(i.ResolvedAt.Value, DateTimeKind.Utc))
            : null,
    };
}
