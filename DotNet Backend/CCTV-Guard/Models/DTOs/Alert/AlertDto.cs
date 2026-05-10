namespace CCTV_Guard.Models.DTOs.Alert;

public class AlertDto
{
    public string Id { get; set; } = string.Empty;
    public string IncidentId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string CameraName { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    // Angular model uses "read" and "dismissed" — match exactly
    public bool Read { get; set; }
    public bool Dismissed { get; set; }
}
