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
    DateTimeOffset PublishedAt);
