namespace TheDailyRSS.Server.Services;

public sealed class FeedOptions
{
    public const string SectionName = "Feeds";

    /// <summary>How often the background fetcher refreshes every feed.</summary>
    public int RefreshIntervalMinutes { get; set; } = 20;

    /// <summary>IANA timezone used to assign articles to an "edition" calendar day.</summary>
    public string EditionTimeZone { get; set; } = "UTC";

    /// <summary>Max articles kept per feed (older ones are pruned). 0 = keep all.</summary>
    public int MaxArticlesPerFeed { get; set; } = 500;
}
