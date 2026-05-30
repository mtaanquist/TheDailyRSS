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

    /// <summary>New key to store; null/empty leaves the existing key untouched.</summary>
    public string? ApiKey { get; set; }

    /// <summary>When true, deletes the stored key (ignores <see cref="ApiKey"/>).</summary>
    public bool ClearApiKey { get; set; }
}

/// <summary>The admin-editable AI "house style" preamble (the editor persona + voice shared by the
/// daily briefing and The Weekly). <see cref="Value"/> is the effective text — the stored override, or
/// the built-in <see cref="Default"/> when none is set; <see cref="IsDefault"/> is true when unmodified.</summary>
public sealed record AiHouseStyleDto(string Value, bool IsDefault, string Default);

/// <summary>Sets the AI house style. A null/blank value clears the override and reverts to the default.</summary>
public sealed class UpdateAiHouseStyleRequest
{
    public string? Value { get; set; }
}

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
