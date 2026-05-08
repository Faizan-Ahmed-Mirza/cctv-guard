using CCTV_Guard.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCTV_Guard.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        await SeedUsersAsync(db);
        await SeedAiSettingsAsync(db);
        await SeedCamerasAsync(db);
        await SeedIncidentsAndAlertsAsync(db);
    }

    // ── Users ─────────────────────────────────────────────────────────────────
    private static async Task SeedUsersAsync(AppDbContext db)
    {
        if (await db.Users.AnyAsync()) return;

        var users = new List<User>
        {
            new() {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Username = "admin",
                Email = "admin@cctvguard.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
                Role = "Admin",
                Status = "active",
                CreatedAt = new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc)
            },
            new() {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Username = "operator",
                Email = "operator@cctvguard.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
                Role = "Operator",
                Status = "active",
                CreatedAt = new DateTime(2024, 2, 15, 0, 0, 0, DateTimeKind.Utc)
            },
            new() {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                Username = "viewer",
                Email = "viewer@cctvguard.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
                Role = "Viewer",
                Status = "active",
                CreatedAt = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new() {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
                Username = "op.ahmed",
                Email = "ahmed@cctvguard.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
                Role = "Operator",
                Status = "active",
                CreatedAt = new DateTime(2024, 4, 20, 0, 0, 0, DateTimeKind.Utc)
            },
            new() {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000005"),
                Username = "op.sara",
                Email = "sara@cctvguard.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
                Role = "Operator",
                Status = "active",
                CreatedAt = new DateTime(2024, 5, 5, 0, 0, 0, DateTimeKind.Utc)
            },
            new() {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000006"),
                Username = "viewer.ali",
                Email = "ali@cctvguard.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
                Role = "Viewer",
                Status = "active",
                CreatedAt = new DateTime(2024, 6, 12, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        db.Users.AddRange(users);
        await db.SaveChangesAsync();
    }

    // ── AI Settings ───────────────────────────────────────────────────────────
    private static async Task SeedAiSettingsAsync(AppDbContext db)
    {
        if (await db.AiSettings.AnyAsync()) return;

        db.AiSettings.Add(new AiSettings { Id = 1 });
        await db.SaveChangesAsync();
    }

    // ── Cameras ───────────────────────────────────────────────────────────────
    private static async Task SeedCamerasAsync(AppDbContext db)
    {
        if (await db.Cameras.AnyAsync()) return;

        var cameras = new List<Camera>
        {
            new() { Id = "cam-01", Name = "Main Entrance",    Location = "Building A - Gate 1",       IpAddress = "192.168.1.101", Port = 554, Status = "online",  DetectionEnabled = true,  ConfidenceThreshold = 0.85m, FrameRate = 30, LastSeen = DateTime.UtcNow },
            new() { Id = "cam-02", Name = "Parking Lot North",Location = "Parking Zone B",             IpAddress = "192.168.1.102", Port = 554, Status = "online",  DetectionEnabled = true,  ConfidenceThreshold = 0.80m, FrameRate = 25, LastSeen = DateTime.UtcNow },
            new() { Id = "cam-03", Name = "Corridor B2",      Location = "Building B - Floor 2",      IpAddress = "192.168.1.103", Port = 554, Status = "online",  DetectionEnabled = true,  ConfidenceThreshold = 0.90m, FrameRate = 30, LastSeen = DateTime.UtcNow },
            new() { Id = "cam-04", Name = "Server Room",      Location = "Data Center",               IpAddress = "192.168.1.104", Port = 554, Status = "offline", DetectionEnabled = false, ConfidenceThreshold = 0.95m, FrameRate = 15, LastSeen = DateTime.UtcNow.AddHours(-1) },
            new() { Id = "cam-05", Name = "Cafeteria",        Location = "Building C - Ground Floor", IpAddress = "192.168.1.105", Port = 554, Status = "online",  DetectionEnabled = true,  ConfidenceThreshold = 0.75m, FrameRate = 20, LastSeen = DateTime.UtcNow },
            new() { Id = "cam-06", Name = "Emergency Exit",   Location = "Building A - Rear",         IpAddress = "192.168.1.106", Port = 554, Status = "error",   DetectionEnabled = true,  ConfidenceThreshold = 0.85m, FrameRate = 30, LastSeen = DateTime.UtcNow.AddMinutes(-10) },
        };

        db.Cameras.AddRange(cameras);
        await db.SaveChangesAsync();
    }

    // ── Incidents + Alerts ────────────────────────────────────────────────────
    private static async Task SeedIncidentsAndAlertsAsync(AppDbContext db)
    {
        if (await db.Incidents.AnyAsync()) return;

        var now = DateTime.UtcNow;

        var incidents = new List<Incident>
        {
            new() { Id = "inc-001", CameraId = "cam-01", Type = "fight",         Severity = "critical", Confidence = 0.94m, Timestamp = now.AddMinutes(-5),   Status = "new",          Notes = "Two individuals engaged in physical altercation near gate." },
            new() { Id = "inc-002", CameraId = "cam-02", Type = "weapon",        Severity = "critical", Confidence = 0.91m, Timestamp = now.AddMinutes(-15),  Status = "acknowledged" },
            new() { Id = "inc-003", CameraId = "cam-03", Type = "unknown_face",  Severity = "high",     Confidence = 0.88m, Timestamp = now.AddMinutes(-30),  Status = "new" },
            new() { Id = "inc-004", CameraId = "cam-05", Type = "intrusion",     Severity = "medium",   Confidence = 0.79m, Timestamp = now.AddHours(-1),     Status = "resolved" },
            new() { Id = "inc-005", CameraId = "cam-01", Type = "license_plate", Severity = "low",      Confidence = 0.96m, Timestamp = now.AddHours(-2),     Status = "resolved" },
            new() { Id = "inc-006", CameraId = "cam-02", Type = "fight",         Severity = "high",     Confidence = 0.87m, Timestamp = now.AddHours(-3),     Status = "acknowledged" },
            new() { Id = "inc-007", CameraId = "cam-03", Type = "intrusion",     Severity = "medium",   Confidence = 0.82m, Timestamp = now.AddHours(-4),     Status = "resolved" },
            new() { Id = "inc-008", CameraId = "cam-05", Type = "unknown_face",  Severity = "low",      Confidence = 0.73m, Timestamp = now.AddHours(-5),     Status = "resolved" },
        };

        db.Incidents.AddRange(incidents);
        await db.SaveChangesAsync();

        var alerts = new List<Alert>
        {
            new() { Id = "alr-001", IncidentId = "inc-001", CameraId = "cam-01", Type = "Fight Detected",        Message = "Physical altercation detected at Main Entrance with 94% confidence.",    Severity = "critical", Timestamp = now.AddMinutes(-5) },
            new() { Id = "alr-002", IncidentId = "inc-002", CameraId = "cam-02", Type = "Weapon Detected",       Message = "Potential weapon brandishing detected in Parking Lot North.",            Severity = "critical", Timestamp = now.AddMinutes(-15) },
            new() { Id = "alr-003", IncidentId = "inc-003", CameraId = "cam-03", Type = "Unknown Individual",    Message = "Unregistered face detected in restricted area Corridor B2.",             Severity = "high",     Timestamp = now.AddMinutes(-30) },
            new() { Id = "alr-004", IncidentId = "inc-004", CameraId = "cam-05", Type = "Unauthorized Intrusion",Message = "Motion detected in restricted zone after hours.",                        Severity = "medium",   Timestamp = now.AddHours(-1) },
            new() { Id = "alr-005", IncidentId = "inc-001", CameraId = "cam-04", Type = "Camera Offline",        Message = "Server Room camera has lost connection. Heartbeat timeout.",             Severity = "high",     Timestamp = now.AddHours(-1).AddMinutes(-2) },
            new() { Id = "alr-006", IncidentId = "inc-001", CameraId = "cam-06", Type = "Camera Error",          Message = "Emergency Exit camera reporting stream errors.",                         Severity = "medium",   Timestamp = now.AddMinutes(-10) },
        };

        db.Alerts.AddRange(alerts);
        await db.SaveChangesAsync();
    }
}
