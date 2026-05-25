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

/// <summary>Which article fields a keyword filter matches against.</summary>
public enum KeywordScope
{
    /// <summary>Match the headline and summary text.</summary>
    TitleAndSummary = 0,
    /// <summary>Match the headline only.</summary>
    TitleOnly = 1,
}
