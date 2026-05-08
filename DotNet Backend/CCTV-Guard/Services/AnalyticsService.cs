using CCTV_Guard.Data;
using CCTV_Guard.Models.DTOs.Analytics;
using Microsoft.EntityFrameworkCore;

namespace CCTV_Guard.Services;

public class AnalyticsService
{
    private readonly AppDbContext _db;

    public AnalyticsService(AppDbContext db) => _db = db;

    public async Task<List<UserSessionSummaryDto>> GetUserSessionsAsync(int year, int? month, string? role)
    {
        var query = _db.UserSessions
            .Include(s => s.User)
            .Where(s => s.LoginAt.Year == year && s.DurationMin != null)
            .AsQueryable();

        if (month.HasValue)
            query = query.Where(s => s.LoginAt.Month == month.Value);
        if (!string.IsNullOrWhiteSpace(role))
            query = query.Where(s => s.User.Role == role);

        var sessions = await query.ToListAsync();

        return sessions
            .GroupBy(s => new { s.UserId, s.User.Username, s.User.Role, s.LoginAt.Year, s.LoginAt.Month })
            .Select(g => new UserSessionSummaryDto
            {
                UserId = g.Key.UserId,
                Username = g.Key.Username,
                Role = g.Key.Role,
                Year = g.Key.Year,
                Month = g.Key.Month,
                TotalSessions = g.Count(),
                TotalHoursActive = Math.Round(g.Sum(s => s.DurationMin ?? 0) / 60.0, 1)
            })
            .OrderBy(s => s.Username)
            .ToList();
    }

    public async Task<List<CameraDetectionStatDto>> GetCameraDetectionsAsync()
    {
        var incidents = await _db.Incidents
            .Include(i => i.Camera)
            .ToListAsync();

        return incidents
            .GroupBy(i => new { i.CameraId, CameraName = i.Camera?.Name ?? i.CameraId })
            .Select(g => new CameraDetectionStatDto
            {
                CameraId = g.Key.CameraId,
                CameraName = g.Key.CameraName,
                TotalDetections = g.Count(),
                FightCount = g.Count(i => i.Type == "fight"),
                WeaponCount = g.Count(i => i.Type == "weapon"),
                IntrusionCount = g.Count(i => i.Type == "intrusion"),
                UnknownFaceCount = g.Count(i => i.Type == "unknown_face"),
                LicensePlateCount = g.Count(i => i.Type == "license_plate")
            })
            .OrderByDescending(c => c.TotalDetections)
            .ToList();
    }

    public async Task<List<MonthlyAlertStatDto>> GetMonthlyAlertsAsync(int year)
    {
        var alerts = await _db.Alerts
            .Where(a => a.Timestamp.Year == year)
            .ToListAsync();

        var monthNames = new[] { "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        return alerts
            .GroupBy(a => a.Timestamp.Month)
            .Select(g => new MonthlyAlertStatDto
            {
                Month = g.Key,
                Year = year,
                Label = $"{monthNames[g.Key]} {year}",
                Total = g.Count(),
                Critical = g.Count(a => a.Severity == "critical"),
                High = g.Count(a => a.Severity == "high"),
                Medium = g.Count(a => a.Severity == "medium"),
                Low = g.Count(a => a.Severity == "low")
            })
            .OrderBy(s => s.Month)
            .ToList();
    }

    public async Task<List<CameraAlertStatDto>> GetCameraAlertsAsync()
    {
        var alerts = await _db.Alerts
            .Include(a => a.Camera)
            .ToListAsync();

        return alerts
            .GroupBy(a => new { a.CameraId, CameraName = a.Camera?.Name ?? a.CameraId })
            .Select(g => new CameraAlertStatDto
            {
                CameraId = g.Key.CameraId,
                CameraName = g.Key.CameraName,
                Total = g.Count(),
                Critical = g.Count(a => a.Severity == "critical"),
                High = g.Count(a => a.Severity == "high")
            })
            .OrderByDescending(c => c.Total)
            .ToList();
    }

    public async Task<AnalyticsOverviewDto> GetOverviewAsync(int year)
    {
        var sessions = await _db.UserSessions
            .Include(s => s.User)
            .Where(s => s.LoginAt.Year == year && s.DurationMin != null)
            .ToListAsync();

        var totalAlerts = await _db.Alerts.CountAsync(a => a.Timestamp.Year == year);
        var totalDetections = await _db.Incidents.CountAsync();

        return new AnalyticsOverviewDto
        {
            TotalActiveHours = Math.Round(sessions.Sum(s => s.DurationMin ?? 0) / 60.0, 1),
            OperatorHours = Math.Round(sessions.Where(s => s.User.Role == "Operator").Sum(s => s.DurationMin ?? 0) / 60.0, 1),
            ViewerHours = Math.Round(sessions.Where(s => s.User.Role == "Viewer").Sum(s => s.DurationMin ?? 0) / 60.0, 1),
            TotalDetections = totalDetections,
            TotalAlerts = totalAlerts
        };
    }
}
