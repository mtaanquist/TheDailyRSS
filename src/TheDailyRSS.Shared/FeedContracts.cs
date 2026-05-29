using System.ComponentModel.DataAnnotations;

namespace TheDailyRSS.Shared;

public sealed record CategoryDto(
    Guid Id,
    string Name,
    string Slug,
    string Color,
    int SortOrder,
    int FeedCount,
    int UnreadCount);

public sealed class CreateCategoryRequest
{
    [Required, MinLength(1), MaxLength(60)]
    public string Name { get; set; } = "";

    [Required, MinLength(1), MaxLength(60)]
    public string Slug { get; set; } = "";

    /// <summary>Hex colour used for the dot/marker in management views.</summary>
    public string Color { get; set; } = "#a83020";
}

public sealed class UpdateCategoryRequest
{
    [Required, MinLength(1), MaxLength(60)]
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#a83020";
}

/// <summary>
/// A user's subscription to a shared feed source. <see cref="Id"/> is the subscription id;
/// <see cref="Title"/> is the per-user override or the source's own title.
/// </summary>
public sealed record FeedDto(
    Guid Id,
    Guid SourceId,
    Guid CategoryId,
    string Title,
    string FeedUrl,
    string? SiteUrl,
    string IconText,
    int SortOrder,
    int UnreadCount,
    int TotalCount,
    DateTimeOffset? LastFetchedAt,
    string? LastFetchError);

public sealed record KeywordFilterDto(
    Guid Id,
    string Term,
    KeywordScope Scope,
    Guid? SourceId,
    string? SourceTitle);

public sealed class CreateKeywordRequest
{
    [Required, MinLength(1), MaxLength(120)]
    public string Term { get; set; } = "";

    public KeywordScope Scope { get; set; } = KeywordScope.Everywhere;

    /// <summary>Limit the rule to a single feed source. Null = applies across every subscription.</summary>
    public Guid? SourceId { get; set; }
}

/// <summary>A structured field-value mute rule. Captures came from the original feed XML
/// (e.g. <c>&lt;category&gt;guides&lt;/category&gt;</c> → <c>FieldKey="category", Value="guides"</c>).
/// <see cref="SourceId"/> scopes the rule to one feed; null means every subscribed feed.</summary>
public sealed record FieldFilterDto(
    Guid Id,
    string FieldKey,
    FieldFilterOperator Operator,
    string Value,
    Guid? SourceId,
    string? SourceTitle);

public sealed class CreateFieldFilterRequest
{
    [Required, MinLength(1), MaxLength(120)]
    public string FieldKey { get; set; } = "";

    [Required, MinLength(1), MaxLength(200)]
    public string Value { get; set; } = "";

    public FieldFilterOperator Operator { get; set; } = FieldFilterOperator.Equals;

    /// <summary>Limit the rule to a single feed source. Null = applies across every subscription.</summary>
    public Guid? SourceId { get; set; }
}

public sealed class AddFeedRequest
{
    /// <summary>A site or feed URL. The server auto-detects the feed if a site URL is given.</summary>
    [Required, Url]
    public string Url { get; set; } = "";

    [Required]
    public Guid CategoryId { get; set; }

    /// <summary>Optional override; otherwise the feed's own title is used.</summary>
    public string? Title { get; set; }

    /// <summary>When true, use <see cref="Url"/> exactly as the feed and skip HTML auto-discovery
    /// (so a specific section feed isn't replaced by the site-wide one the page advertises).</summary>
    public bool Exact { get; set; }
}

public sealed class UpdateFeedRequest
{
    [Required, MinLength(1)]
    public string Title { get; set; } = "";

    [Required]
    public Guid CategoryId { get; set; }
}

/// <summary>Preview returned by the add-feed auto-detect step.</summary>
public sealed record FeedDetectResult(
    bool Found,
    string? FeedUrl,
    string? Title,
    string? SiteUrl,
    string? IconText,
    string[] RecentHeadlines);

/// <summary>Result of an OPML import.</summary>
public sealed record OpmlImportResult(int CategoriesCreated, int FeedsAdded, int Skipped);

/// <summary>Reorder payload — a flat ordered list of ids within a parent.</summary>
public sealed class ReorderRequest
{
    public List<Guid> OrderedIds { get; set; } = new();
}

/// <summary>Move a feed to a different category (drag between folders).</summary>
public sealed class MoveFeedRequest
{
    [Required]
    public Guid CategoryId { get; set; }
}
