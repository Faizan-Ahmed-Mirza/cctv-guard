namespace CCTV_Guard.Models.Entities;

public class Camera
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 554;
    public string Status { get; set; } = "offline"; // online | offline | error
    public string? StreamUrl { get; set; }      // HLS URL served by this backend
    public string? RtspUrl { get; set; }        // raw RTSP URL from the camera
    public bool DetectionEnabled { get; set; } = true;
    public decimal ConfidenceThreshold { get; set; } = 0.85m;
    public int FrameRate { get; set; } = 30;
    public DateTime? LastSeen { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;  // soft delete — preserves incident history

    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
}
