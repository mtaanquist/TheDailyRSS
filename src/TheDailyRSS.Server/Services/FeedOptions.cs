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

    /// <summary>Hard cap on a single fetched feed/page response body. Guards against a hostile or
    /// runaway endpoint exhausting memory. Applies to feed fetches, discovery scrapes and OPML import.</summary>
    public int MaxResponseBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>Wall-clock timeout for a single (non-streaming) BYOK AI call. Generous by default because a
    /// self-hosted reasoning model on a full daily/weekly corpus can legitimately take minutes; the call runs
    /// off the request thread (background worker), so this only needs to exceed a real generation. The call
    /// always terminates at this bound with a recorded error rather than hanging.</summary>
    public int AiRequestTimeoutSeconds { get; set; } = 600;

    // ── Full-article (reader-mode) extraction ────────────────────────────
    // When a source has FetchFullContent on, the fetcher steps into each article's page. These
    // bound how aggressively we scrape so we stay a polite citizen and don't get blocked.

    /// <summary>Delay between successive article-page fetches to the same host (reader-mode
    /// extraction). Kept conservative so we aren't rate-limited/blocked; slow is acceptable.</summary>
    public int FullContentDelaySeconds { get; set; } = 5;

    /// <summary>How many already-stored articles the backfill worker extracts per source per tick.</summary>
    public int FullContentBackfillBatchSize { get; set; } = 20;

    /// <summary>How often the backfill worker scans for articles still missing full content.</summary>
    public int FullContentBackfillIntervalMinutes { get; set; } = 2;

    /// <summary>Cap on how many newly-fetched articles get reader-mode extraction inline during a
    /// refresh (the rest are left to the backfill worker, keeping the synchronous Add fast).</summary>
    public int FullContentInlineLimit { get; set; } = 5;

    // ── Third-party data APIs (weather, tickers, …) ─────────────────────

    /// <summary>Wall-clock timeout for a single call to a fixed third-party JSON API (via
    /// <see cref="ExternalApiClient"/>). Short — these are quick metadata calls, fetched on a schedule.</summary>
    public int ExternalApiTimeoutSeconds { get; set; } = 15;

    /// <summary>How often the background worker refreshes today's weather for each watched location (#33).</summary>
    public int WeatherRefreshIntervalMinutes { get; set; } = 60;

    /// <summary>How often the background worker refreshes the quote for each tracked stock ticker (#32).</summary>
    public int TickerRefreshIntervalMinutes { get; set; } = 5;
}
