namespace TheDailyRSS.Shared;

/// <summary>The user's BYOK LLM configuration as shown on the settings screen. The API key is
/// never returned — only <see cref="HasApiKey"/> tells the client whether one is stored.</summary>
public sealed record AiSettingsDto(
    bool Enabled,
    string? BaseUrl,
    string? Model,
    string? SystemPrompt,
    bool AutoDaily,
    bool AutoWeekly,
    bool AutoArticle,
    bool HasApiKey);

/// <summary>Updates the BYOK config. <see cref="ApiKey"/> is write-only: a non-empty value sets
/// the key, an empty/null value leaves the stored key unchanged, and <see cref="ClearApiKey"/>
/// removes it.</summary>
public sealed class UpdateAiSettingsRequest
{
    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
    public string? SystemPrompt { get; set; }
    public bool AutoDaily { get; set; }
    public bool AutoWeekly { get; set; }

    /// <summary>Pre-generate a per-article TL;DR for articles from full-text feeds in the background.</summary>
    public bool AutoArticle { get; set; }

    /// <summary>New key to store; null/empty leaves the existing key untouched.</summary>
    public string? ApiKey { get; set; }

    /// <summary>When true, deletes the stored key (ignores <see cref="ApiKey"/>).</summary>
    public bool ClearApiKey { get; set; }
}

/// <summary>The admin-editable AI "house style" preamble (the editor persona + voice shared by the
/// daily briefing and The Weekly). <see cref="Value"/> is the effective text — the stored override, or
/// the built-in <see cref="Default"/> when none is set; <see cref="IsDefault"/> is true when unmodified.
/// <see cref="DailyRules"/> and <see cref="WeeklyRules"/> are the fixed, code-owned instructions appended
/// after the house style — shown read-only so the admin can see the whole assembled prompt.</summary>
public sealed record AiHouseStyleDto(
    string Value,
    bool IsDefault,
    string Default,
    string DailyRules,
    string WeeklyRules);

/// <summary>Sets the AI house style. A null/blank value clears the override and reverts to the default.</summary>
public sealed class UpdateAiHouseStyleRequest
{
    public string? Value { get; set; }
}

/// <summary>An AI generation that's running right now, for the admin activity view. <see cref="Kind"/> is
/// "Daily"/"Weekly"/"Article"; <see cref="Trigger"/> is "Scheduled" (nightly worker) or "Interactive"
/// (a reader clicked generate). <see cref="ElapsedSeconds"/> is how long it's been running.</summary>
public sealed record AiJobDto(
    string User,
    string Kind,
    string Trigger,
    string? Label,
    DateTimeOffset StartedAt,
    int ElapsedSeconds);

/// <summary>A recorded AI failure for the admin error log. <see cref="Message"/> is the raw error text,
/// surfaced verbatim so the admin can read exactly what went wrong.</summary>
public sealed record AiErrorDto(
    DateTimeOffset OccurredAt,
    string User,
    string Kind,
    string Trigger,
    string? Label,
    string Message);

/// <summary>What the caller's own AI generation is doing right now, for the manual-generate poll loop.
/// <see cref="Running"/> lists the kinds ("Daily"/"Weekly") currently queued or running for this user;
/// <see cref="LastError"/> is the reader's most recent failure, so a poll that ends without a result can
/// explain why.</summary>
public sealed record AiActivityDto(
    IReadOnlyList<string> Running,
    AiErrorDto? LastError);

/// <summary>A per-user AI TL;DR of a single article (distinct from <see cref="ArticleSummaryDto"/>,
/// which is an article card in an edition). Returned when one is generated on demand.</summary>
public sealed record ArticleAiSummaryDto(
    string Content,
    string Model,
    DateTimeOffset GeneratedAt);

/// <summary>A generated AI digest over a date range.</summary>
public sealed record AiSummaryDto(
    AiSummaryKind Kind,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Content,
    string Model,
    int ArticleCount,
    DateTimeOffset GeneratedAt);

/// <summary>"The Weekly": an AI-curated front page for the past week. The agent picks the most
/// important stories from each category and writes the masthead; the selected stories are
/// re-projected to live <see cref="ArticleSummaryDto"/>s so per-user read/saved state stays fresh.</summary>
public sealed record WeeklyEditionDto(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    /// <summary>The AI-written masthead headline (a short phrase capturing the week).</summary>
    string Headline,
    /// <summary>The AI editor's note introducing the edition (Markdown).</summary>
    string Intro,
    string Model,
    int ArticleCount,
    DateTimeOffset GeneratedAt,
    ArticleSummaryDto? Lead,
    IReadOnlyList<EditionSectionDto> Sections);
