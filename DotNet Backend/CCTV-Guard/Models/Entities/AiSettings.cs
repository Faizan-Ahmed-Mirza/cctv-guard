using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CCTV_Guard.Models.Entities;

public class AiSettings
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)] // we supply Id = 1, not the DB
    public int Id { get; set; } = 1;
    public bool FightDetection { get; set; } = true;
    public bool WeaponDetection { get; set; } = true;
    public bool IntrusionDetection { get; set; } = true;
    public bool FaceRecognition { get; set; } = true;
    public bool LicensePlate { get; set; } = true;
    public decimal GlobalConfidence { get; set; } = 0.25m;
    public int AlertLatencyTarget { get; set; } = 2;
    public int FrameProcessingRate { get; set; } = 30;
    public bool GpuAcceleration { get; set; } = true;
    public string ModelVersion { get; set; } = "YOLOv8n";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedBy { get; set; }
}
