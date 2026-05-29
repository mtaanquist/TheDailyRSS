namespace TheDailyRSS.Client.Services;

/// <summary>Human-friendly elapsed-time strings. Two flavours are used by the UI: a terse
/// "5m/3h/2d" badge for article kickers and a spoken "5 min ago" form for the devices list.</summary>
public static class RelativeTime
{
    /// <summary>Terse form for dense lists: "just now", "5m", "3h", "2d", then "MMM d".</summary>
    public static string Terse(DateTimeOffset when)
    {
        var d = Elapsed(when);
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h";
        if (d.TotalDays < 7) return $"{(int)d.TotalDays}d";
        return when.ToString("MMM d");
    }

    /// <summary>Spoken form: "now", "5 min ago", "3h ago", "2d ago".</summary>
    public static string Ago(DateTimeOffset when)
    {
        var d = Elapsed(when);
        if (d.TotalMinutes < 1) return "now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h ago";
        return $"{(int)d.TotalDays}d ago";
    }

    private static TimeSpan Elapsed(DateTimeOffset when)
    {
        var d = DateTimeOffset.Now - when;
        return d < TimeSpan.Zero ? TimeSpan.Zero : d;
    }
}
