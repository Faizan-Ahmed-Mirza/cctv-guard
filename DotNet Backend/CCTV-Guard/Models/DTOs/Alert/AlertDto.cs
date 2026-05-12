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
    public string? ImageUrl { get; set; }
    // Angular model uses "read" and "dismissed" — match exactly
    public bool Read { get; set; }
    public bool Dismissed { get; set; }
    /// <summary>True when an operator has escalated this alert to emergency services.</summary>
    public bool IsEscalated { get; set; }
}

/// <summary>Payload broadcast to Flutter mobile clients when an alert is escalated.</summary>
public class EmergencyNotificationDto
{
    public string AlertId { get; set; } = string.Empty;
    public string IncidentId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string CameraName { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string? ImageUrl { get; set; }
    /// <summary>Username of the operator who escalated.</summary>
    public string EscalatedBy { get; set; } = string.Empty;
    public DateTimeOffset EscalatedAt { get; set; }
}
