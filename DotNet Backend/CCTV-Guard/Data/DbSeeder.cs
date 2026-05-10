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

        // AiSettings must always exist (Id=1) — SettingsController does FindAsync(1)
        // and the Configuration page breaks if this row is missing.
        if (!await db.AiSettings.AnyAsync())
        {
            db.AiSettings.Add(new AiSettings { Id = 1 });
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
