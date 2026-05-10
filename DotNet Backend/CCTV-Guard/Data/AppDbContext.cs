using Microsoft.EntityFrameworkCore;
using CCTV_Guard.Models.Entities;

namespace CCTV_Guard.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AlertReadStatus> AlertReadStatuses => Set<AlertReadStatus>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<AiSettings> AiSettings => Set<AiSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Username).HasMaxLength(50).IsRequired();
            e.Property(u => u.Email).HasMaxLength(150).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();
            e.Property(u => u.Role).HasMaxLength(20).IsRequired();
            e.Property(u => u.Status).HasMaxLength(20).IsRequired().HasDefaultValue("active");
        });

        // RefreshToken
        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.Token).IsUnique();
            e.Property(r => r.Token).HasMaxLength(500).IsRequired();
            e.HasOne(r => r.User)
             .WithMany(u => u.RefreshTokens)
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Camera
        modelBuilder.Entity<Camera>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasMaxLength(50);
            e.Property(c => c.Name).HasMaxLength(100).IsRequired();
            e.Property(c => c.Location).HasMaxLength(200).IsRequired();
            e.Property(c => c.IpAddress).HasMaxLength(50).IsRequired();
            e.Property(c => c.Status).HasMaxLength(20).IsRequired().HasDefaultValue("offline");
            e.Property(c => c.StreamUrl).HasMaxLength(500);
            e.Property(c => c.ConfidenceThreshold).HasPrecision(4, 2);
            e.Property(c => c.IsDeleted).HasDefaultValue(false);
            // Global query filter — deleted cameras are invisible to all queries
            e.HasQueryFilter(c => !c.IsDeleted);
        });

        // Incident
        modelBuilder.Entity<Incident>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasMaxLength(50);
            e.Property(i => i.Type).HasMaxLength(30).IsRequired();
            e.Property(i => i.Severity).HasMaxLength(20).IsRequired();
            e.Property(i => i.Confidence).HasPrecision(5, 4);
            e.Property(i => i.Status).HasMaxLength(20).IsRequired().HasDefaultValue("new");
            e.Property(i => i.Notes).HasMaxLength(1000);
            // ThumbnailUrl stores a base64 data URL (~100KB+) — no length limit
            e.Property(i => i.ThumbnailUrl).HasColumnType("nvarchar(max)");
            e.HasOne(i => i.Camera)
             .WithMany(c => c.Incidents)
             .HasForeignKey(i => i.CameraId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // Alert
        modelBuilder.Entity<Alert>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasMaxLength(50);
            e.Property(a => a.Type).HasMaxLength(100).IsRequired();
            e.Property(a => a.Message).HasMaxLength(500).IsRequired();
            e.Property(a => a.Severity).HasMaxLength(20).IsRequired();
            e.HasOne(a => a.Incident)
             .WithMany(i => i.Alerts)
             .HasForeignKey(a => a.IncidentId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Camera)
             .WithMany()
             .HasForeignKey(a => a.CameraId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // AlertReadStatus
        modelBuilder.Entity<AlertReadStatus>(e =>
        {
            e.HasKey(ar => ar.Id);
            e.HasIndex(ar => new { ar.AlertId, ar.UserId }).IsUnique();
            e.HasOne(ar => ar.Alert)
             .WithMany(a => a.ReadStatuses)
             .HasForeignKey(ar => ar.AlertId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ar => ar.User)
             .WithMany(u => u.AlertReadStatuses)
             .HasForeignKey(ar => ar.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // UserSession
        modelBuilder.Entity<UserSession>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.IpAddress).HasMaxLength(50);
            e.Property(s => s.UserAgent).HasMaxLength(500);
            e.HasOne(s => s.User)
             .WithMany(u => u.Sessions)
             .HasForeignKey(s => s.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // AiSettings — single row
        modelBuilder.Entity<AiSettings>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.ModelVersion).HasMaxLength(20);
            e.Property(a => a.GlobalConfidence).HasPrecision(4, 2);
        });
    }
}
