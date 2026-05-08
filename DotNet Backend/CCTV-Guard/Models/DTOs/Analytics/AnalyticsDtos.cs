namespace CCTV_Guard.Models.DTOs.Analytics;

public class UserSessionSummaryDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public int TotalSessions { get; set; }
    public double TotalHoursActive { get; set; }
}

public class CameraDetectionStatDto
{
    public string CameraId { get; set; } = string.Empty;
    public string CameraName { get; set; } = string.Empty;
    public int TotalDetections { get; set; }
    public int FightCount { get; set; }
    public int WeaponCount { get; set; }
    public int IntrusionCount { get; set; }
    public int UnknownFaceCount { get; set; }
    public int LicensePlateCount { get; set; }
}

public class MonthlyAlertStatDto
{
    public int Month { get; set; }
    public int Year { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Critical { get; set; }
    public int High { get; set; }
    public int Medium { get; set; }
    public int Low { get; set; }
}

public class CameraAlertStatDto
{
    public string CameraId { get; set; } = string.Empty;
    public string CameraName { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Critical { get; set; }
    public int High { get; set; }
}

public class AnalyticsOverviewDto
{
    public double TotalActiveHours { get; set; }
    public double OperatorHours { get; set; }
    public double ViewerHours { get; set; }
    public int TotalDetections { get; set; }
    public int TotalAlerts { get; set; }
}
