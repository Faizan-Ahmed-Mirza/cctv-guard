using System.ComponentModel.DataAnnotations;

namespace CCTV_Guard.Models.DTOs.Camera;

public class CameraDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? StreamUrl { get; set; }
    public string? RtspUrl { get; set; }
    public bool DetectionEnabled { get; set; }
    public decimal ConfidenceThreshold { get; set; }
    public int FrameRate { get; set; }
    public DateTime? LastSeen { get; set; }
}

public class CreateCameraRequest
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Location { get; set; } = string.Empty;
    [Required] public string IpAddress { get; set; } = string.Empty;
    [Range(1, 65535)] public int Port { get; set; } = 554;
    public bool DetectionEnabled { get; set; } = true;
    [Range(0.0, 1.0)] public decimal ConfidenceThreshold { get; set; } = 0.85m;
    [Range(1, 120)] public int FrameRate { get; set; } = 30;
    public string? StreamUrl { get; set; }
    public string? RtspUrl { get; set; }
}

public class UpdateCameraRequest : CreateCameraRequest
{
    public string Status { get; set; } = "offline";
}

public class PatchDetectionRequest
{
    public bool DetectionEnabled { get; set; }
}
