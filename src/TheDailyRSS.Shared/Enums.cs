namespace TheDailyRSS.Shared;

/// <summary>Reading theme. Auto switches by local sunset (client-evaluated).</summary>
public enum ThemePreference
{
    Newsprint = 0,
    Evening = 1,
    Auto = 2,
}

/// <summary>Headline typeface options offered on the profile screen.</summary>
public enum HeadlineFont
{
    PtSerif = 0,
    Newsreader = 1,
    Lora = 2,
}

/// <summary>How tightly the edition packs headlines + previews.</summary>
public enum ReadingDensity
{
    Compact = 0,
    Balanced = 1,
    Airy = 2,
}

/// <summary>The period an AI digest covers.</summary>
public enum AiSummaryKind
{
    /// <summary>A single edition day.</summary>
    Daily = 0,
    /// <summary>A 7-day range ("The Weekly").</summary>
    Weekly = 1,
}

/// <summary>Which article fields a keyword filter matches against.</summary>
public enum KeywordScope
{
    /// <summary>Match the headline, summary, and full article text (including link URLs).</summary>
    Everywhere = 0,
    /// <summary>Match the headline only.</summary>
    TitleOnly = 1,
}

/// <summary>How a <c>FieldFilter</c>'s value is compared against captured feed-item field values.
/// More operators may follow; today only exact (case-insensitive) match is supported.</summary>
public enum FieldFilterOperator
{
    /// <summary>The captured value equals the filter value (case-insensitive).</summary>
    Equals = 0,
}
