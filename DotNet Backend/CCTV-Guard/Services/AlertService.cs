using CCTV_Guard.Data;
using CCTV_Guard.Models.DTOs.Alert;
using CCTV_Guard.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCTV_Guard.Services;

public class AlertService
{
    private readonly AppDbContext _db;

    public AlertService(AppDbContext db) => _db = db;

    public async Task<List<AlertDto>> GetAllAsync(Guid userId, string? severity, bool dismissed)
    {
        var query = _db.Alerts
            .Include(a => a.Camera)
            .Include(a => a.ReadStatuses.Where(r => r.UserId == userId))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(severity))
            query = query.Where(a => a.Severity == severity);

        var alerts = await query.OrderByDescending(a => a.Timestamp).ToListAsync();

        return alerts.Select(a =>
        {
            var rs = a.ReadStatuses.FirstOrDefault(r => r.UserId == userId);
            var dto = new AlertDto
            {
                Id = a.Id,
                IncidentId = a.IncidentId,
                Type = a.Type,
                Message = a.Message,
                CameraName = a.Camera?.Name ?? string.Empty,
                Severity = a.Severity,
                Timestamp = new DateTimeOffset(DateTime.SpecifyKind(a.Timestamp, DateTimeKind.Utc)),
                Read = rs?.IsRead ?? false,
                Dismissed = rs?.IsDismissed ?? false
            };
            return dto;
        })
        .Where(a => a.Dismissed == dismissed)
        .ToList();
    }

    public async Task MarkReadAsync(string alertId, Guid userId)
    {
        var rs = await GetOrCreateReadStatus(alertId, userId);
        rs.IsRead = true;
        rs.ReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task MarkAllReadAsync(Guid userId)
    {
        var alertIds = await _db.Alerts.Select(a => a.Id).ToListAsync();
        foreach (var alertId in alertIds)
        {
            var rs = await GetOrCreateReadStatus(alertId, userId);
            rs.IsRead = true;
            rs.ReadAt ??= DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }

    public async Task DismissAsync(string alertId, Guid userId)
    {
        var rs = await GetOrCreateReadStatus(alertId, userId);
        rs.IsDismissed = true;
        rs.IsRead = true;
        rs.DismissedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private async Task<AlertReadStatus> GetOrCreateReadStatus(string alertId, Guid userId)
    {
        var rs = await _db.AlertReadStatuses
            .FirstOrDefaultAsync(r => r.AlertId == alertId && r.UserId == userId);

        if (rs == null)
        {
            rs = new AlertReadStatus { AlertId = alertId, UserId = userId };
            _db.AlertReadStatuses.Add(rs);
        }
        return rs;
    }
}
