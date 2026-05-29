namespace TheDailyRSS.Server.Services;

/// <summary>
/// Resolves the configured edition timezone and the calendar "edition day" used to bucket
/// articles. Centralised so the timezone fallback and the day computation live in one place
/// (previously duplicated across the edition endpoints, feed fetch and the AI sweep).
/// </summary>
public static class EditionClock
{
    /// <summary>The configured IANA timezone, falling back to UTC if it can't be resolved.</summary>
    public static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Utc; }
    }

    /// <summary>Today's edition date in the configured timezone.</summary>
    public static DateOnly Today(FeedOptions opts) => EditionDate(DateTimeOffset.UtcNow, opts.EditionTimeZone);

    /// <summary>The edition date an instant falls on, in the given timezone.</summary>
    public static DateOnly EditionDate(DateTimeOffset instant, string timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, ResolveTimeZone(timeZoneId)).DateTime);
}
