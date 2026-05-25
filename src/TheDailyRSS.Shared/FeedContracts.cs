using System.ComponentModel.DataAnnotations;

namespace TheDailyRSS.Shared;

public sealed record CategoryDto(
    Guid Id,
    string Name,
    string Color,
    int SortOrder,
    int FeedCount,
    int UnreadCount);

public sealed class CreateCategoryRequest
{
    [Required, MinLength(1), MaxLength(60)]
    public string Name { get; set; } = "";

    /// <summary>Hex colour used for the dot/marker in management views.</summary>
    public string Color { get; set; } = "#a83020";
}

public sealed class UpdateCategoryRequest
{
    [Required, MinLength(1), MaxLength(60)]
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#a83020";
}

public sealed record FeedDto(
    Guid Id,
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

public sealed class AddFeedRequest
{
    /// <summary>A site or feed URL. The server auto-detects the feed if a site URL is given.</summary>
    [Required, Url]
    public string Url { get; set; } = "";

    [Required]
    public Guid CategoryId { get; set; }

    /// <summary>Optional override; otherwise the feed's own title is used.</summary>
    public string? Title { get; set; }
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
