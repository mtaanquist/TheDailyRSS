namespace TheDailyRSS.Shared;

/// <summary>A headline-level article, used in edition grids and rails.</summary>
public sealed record ArticleSummaryDto(
    Guid Id,
    string Title,
    string? Summary,
    string FeedTitle,
    string FeedIconText,
    Guid CategoryId,
    string CategoryName,
    string CategoryColor,
    string? ImageUrl,
    DateTimeOffset PublishedAt,
    bool IsRead,
    bool IsSaved,
    bool IsHidden,
    string Url);

/// <summary>Full article for the reading pane.</summary>
public sealed record ArticleDto(
    Guid Id,
    string Title,
    string? Summary,
    string? ContentHtml,
    string? Author,
    string FeedTitle,
    string FeedIconText,
    Guid CategoryId,
    string CategoryName,
    string CategoryColor,
    string? ImageUrl,
    DateTimeOffset PublishedAt,
    bool IsRead,
    bool IsSaved,
    bool IsHidden,
    int ReadingPositionPercent,
    string Url,
    Guid SourceId,
    /// <summary>Structured fields lifted from the source feed item — drives the "filter
    /// from this article" affordance. Keys like <c>category</c>, <c>dc:creator</c>,
    /// <c>media:keywords</c>; each maps to one or more captured values.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> Fields,
    /// <summary>The reader's cached AI TL;DR for this article, shown pinned at the top. Null when
    /// none has been generated yet.</summary>
    string? AiSummary);

/// <summary>One day that has at least one article — used by the archive picker.</summary>
public sealed record EditionDateDto(DateOnly Date, int ArticleCount, int UnreadCount);

/// <summary>The public share link generated for an article. <see cref="Url"/> is the absolute,
/// anonymized page a reader can hand to a friend; <see cref="Token"/> is its opaque identifier.</summary>
public sealed record ShareLinkDto(Guid Token, string Url);

/// <summary>A minimal article reference (id + headline) for navigation links.</summary>
public sealed record ArticleLinkDto(Guid Id, string Title);

/// <summary>The stories on either side of the one being read, within the same edition.</summary>
public sealed record ArticleNeighborsDto(ArticleLinkDto? Prev, ArticleLinkDto? Next);

/// <summary>A category's slice of an edition (a "section" of the paper).</summary>
public sealed record EditionSectionDto(
    Guid CategoryId,
    string Name,
    string Color,
    int Count,
    IReadOnlyList<ArticleSummaryDto> Articles);

/// <summary>
/// A single day's edition: a lead story plus the day's articles, grouped into
/// sections. <see cref="CategoryId"/> is set when the reader has drilled into one folder.
/// </summary>
public sealed record EditionDto(
    DateOnly Date,
    string VolumeLabel,
    string IssueLabel,
    string DateLabel,
    bool IsToday,
    DateOnly? PrevDate,
    DateOnly? NextDate,
    int UnreadTotal,
    Guid? CategoryId,
    string? CategoryName,
    ArticleSummaryDto? Lead,
    IReadOnlyList<ArticleSummaryDto> Articles,
    IReadOnlyList<EditionSectionDto> Sections);

/// <summary>Aggregate counts shown on the Sync &amp; devices screen.</summary>
public sealed record SyncStatusDto(
    int FeedCount,
    int CategoryCount,
    int ArticlesTracked,
    int SavedCount,
    int ReadingPositions,
    DateTimeOffset? LastSyncAt);
