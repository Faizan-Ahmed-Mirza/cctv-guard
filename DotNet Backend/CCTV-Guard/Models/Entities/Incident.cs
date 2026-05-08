namespace CCTV_Guard.Models.Entities;

public class Incident
{
    public string Id { get; set; } = string.Empty;
    public string CameraId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // fight | weapon | intrusion | unknown_face | license_plate
    public string Severity { get; set; } = string.Empty; // critical | high | medium | low
    public decimal Confidence { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? ThumbnailUrl { get; set; }
    public int? BoundingBoxX { get; set; }
    public int? BoundingBoxY { get; set; }
    public int? BoundingBoxW { get; set; }
    public int? BoundingBoxH { get; set; }
    public string Status { get; set; } = "new"; // new | acknowledged | resolved
    public string? Notes { get; set; }
    public Guid? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public Camera Camera { get; set; } = null!;
    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
}
