using CCTV_Guard.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCTV_Guard.Data;

/// <summary>
/// Seeds the minimum system configuration and a default admin account
/// required for the app to function on first run.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // ── Fix ThumbnailUrl column — must be nvarchar(max) to store base64 JPEGs ──
        // The original schema had nvarchar(500) which silently truncates/rejects
        // base64 image data (~100KB+), causing SaveChangesAsync to throw and
        // swallowing the incident+alert entirely. Run this on every startup — it's
        // a no-op if the column is already nvarchar(max).
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'Incidents'
                      AND COLUMN_NAME = 'ThumbnailUrl'
                      AND CHARACTER_MAXIMUM_LENGTH = 500
                )
                BEGIN
                    ALTER TABLE Incidents ALTER COLUMN ThumbnailUrl nvarchar(max) NULL;
                END
            ");
        }
        catch { /* ignore — column may already be correct */ }

        // ── Create FacialEmbeddings table if it doesn't exist ──────────────────
        // This table was added after initial DB creation, so we create it manually
        // rather than requiring a full EF migration.
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'FacialEmbeddings')
                BEGIN
                    CREATE TABLE FacialEmbeddings (
                        Id             INT IDENTITY(1,1) PRIMARY KEY,
                        Username       NVARCHAR(50)   NOT NULL,
                        EmbeddingJson  NVARCHAR(MAX)  NOT NULL,
                        RegisteredAt   DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
                        RegisteredBy   NVARCHAR(50)   NOT NULL DEFAULT '',
                        CONSTRAINT UQ_FacialEmbeddings_Username UNIQUE (Username)
                    );
                END
            ");
        }
        catch { /* ignore — table may already exist */ }

        // AiSettings must always exist (Id=1) — SettingsController does FindAsync(1)
        // and the Configuration page breaks if this row is missing.
        if (!await db.AiSettings.AnyAsync())
        {
            db.AiSettings.Add(new AiSettings { Id = 1 });
            await db.SaveChangesAsync();
        }

        // ── Add IsEscalated / EscalatedAt columns to AlertReadStatuses if missing ──
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'AlertReadStatuses' AND COLUMN_NAME = 'IsEscalated'
                )
                BEGIN
                    ALTER TABLE AlertReadStatuses ADD IsEscalated BIT NOT NULL DEFAULT 0;
                    ALTER TABLE AlertReadStatuses ADD EscalatedAt DATETIME2 NULL;
                END
            ");
        }
        catch { /* ignore — columns may already exist */ }
        // 0.85 blocks almost all detections — reset to 0.25 which is the correct working value.
        var highThresholdCameras = await db.Cameras
            .Where(c => c.ConfidenceThreshold >= 0.80m)
            .ToListAsync();
        if (highThresholdCameras.Count > 0)
        {
            foreach (var cam in highThresholdCameras)
                cam.ConfidenceThreshold = 0.25m;
            await db.SaveChangesAsync();
        }

        // Seed a default admin user if no users exist.
        // Credentials: admin / Admin@1234
        // Change the password after first login via the Users page.
        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new User
            {
                Id           = Guid.NewGuid(),
                Username     = "admin",
                Email        = "admin@cctvguard.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@1234"),
                Role         = "Admin",
                Status       = "active",
                CreatedAt    = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }
}
