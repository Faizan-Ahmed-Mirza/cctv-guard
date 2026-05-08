namespace CCTV_Guard.Models.Entities;

public class Alert
{
    public string Id { get; set; } = string.Empty;
    public string IncidentId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string CameraId { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty; // critical | high | medium | low
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public Incident Incident { get; set; } = null!;
    public Camera Camera { get; set; } = null!;
    public ICollection<AlertReadStatus> ReadStatuses { get; set; } = new List<AlertReadStatus>();
}
