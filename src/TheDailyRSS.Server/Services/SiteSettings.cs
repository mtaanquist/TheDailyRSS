using Microsoft.EntityFrameworkCore;
using TheDailyRSS.Server.Data;

namespace TheDailyRSS.Server.Services;

/// <summary>Helpers for reading instance-wide <see cref="AppSetting"/> flags, so the same key isn't
/// queried with subtly different fallbacks in several places.</summary>
public static class SiteSettings
{
    /// <summary>Whether an admin has turned article sharing off for the whole instance. An absent or
    /// blank row means enabled (the default), matching the "no seed needed" AppSetting convention.</summary>
    public static async Task<bool> IsSharingDisabledAsync(AppDbContext db, CancellationToken ct)
    {
        var value = await db.AppSettings
            .Where(s => s.Key == SiteSettingKeys.SharingDisabled)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
