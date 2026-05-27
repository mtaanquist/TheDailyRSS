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

/// <summary>A generated AI digest over a date range.</summary>
public sealed record AiSummaryDto(
    AiSummaryKind Kind,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Content,
    string Model,
    int ArticleCount,
    DateTimeOffset GeneratedAt);
