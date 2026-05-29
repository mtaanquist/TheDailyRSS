namespace TheDailyRSS.Server.Services;

/// <summary>A feed parsed into a transport-neutral shape, ready to upsert.</summary>
public sealed record ParsedFeed(
    string Title,
    string? SiteUrl,
    IReadOnlyList<ParsedItem> Items);

public sealed record ParsedItem(
    string ExternalId,
    string Title,
    string Url,
    string? Author,
    string? Summary,
    string? ContentHtml,
    string? ImageUrl,
    DateTimeOffset PublishedAt,
    /// <summary>Structured fields lifted from the feed XML (category/dc:creator/media:keywords/
    /// custom-namespace leaves). Keys and values are normalised to lower-case so they round-trip
    /// safely through the case-sensitive JSONB containment operator at filter time.</summary>
    Dictionary<string, List<string>> Fields);
