namespace CCTV_Guard.Models.Entities;

public class AlertReadStatus
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AlertId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public bool IsRead { get; set; } = false;
    public bool IsDismissed { get; set; } = false;
    public bool IsEscalated { get; set; } = false;
    public DateTime? ReadAt { get; set; }
    public DateTime? DismissedAt { get; set; }
    public DateTime? EscalatedAt { get; set; }

    public Alert Alert { get; set; } = null!;
    public User User { get; set; } = null!;
}
