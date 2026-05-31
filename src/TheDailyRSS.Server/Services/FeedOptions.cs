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

    /// <summary>Inactivity timeout for the streamed BYOK AI response: we abort only after this many
    /// seconds pass with no new data from the model, reset on every chunk. So it bounds a stall — not
    /// total generation time — and only needs to exceed the gap before the first token (model load +
    /// prompt eval). Generous by default for slow local reasoning models.</summary>
    public int AiRequestTimeoutSeconds { get; set; } = 300;

    /// <summary>Absolute ceiling on a single AI call, regardless of activity. The inactivity timeout alone
    /// can't bound a model that streams continuously (e.g. a reasoning model that never stops "thinking",
    /// whose token trickle keeps resetting the idle timer) — this guarantees the call always terminates
    /// with a recorded error instead of hanging forever. Set comfortably above a normal generation.</summary>
    public int AiMaxRequestSeconds { get; set; } = 600;

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
}
