using CCTV_Guard.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCTV_Guard.Data;

/// <summary>
/// Seeds only the minimum system configuration required for the app to function.
/// All real data (users, cameras, incidents, alerts) is managed through the UI.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // AiSettings must always exist (Id=1) — SettingsController does FindAsync(1)
        // and the Configuration page breaks if this row is missing.
        if (!await db.AiSettings.AnyAsync())
        {
            db.AiSettings.Add(new AiSettings { Id = 1 });
            await db.SaveChangesAsync();
        }
    }
}
