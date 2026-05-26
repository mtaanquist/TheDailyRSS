using Microsoft.AspNetCore.Identity;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Data;

/// <summary>Application user. Preferences live here so they sync across devices.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ThemePreference Theme { get; set; } = ThemePreference.Newsprint;
    public HeadlineFont HeadlineFont { get; set; } = HeadlineFont.PtSerif;
    public ReadingDensity Density { get; set; } = ReadingDensity.Balanced;
    public bool ShowUnread { get; set; } = true;

    public List<Subscription> Subscriptions { get; set; } = new();
    public List<UserArticleState> ArticleStates { get; set; } = new();
    public List<KeywordFilter> KeywordFilters { get; set; } = new();
    public List<UserSession> Sessions { get; set; } = new();
}

/// <summary>
/// A fixed newspaper section. The taxonomy is global and seeded (Guardian-style);
/// users file their subscriptions into these but cannot create their own.
/// </summary>
public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Stable identifier used for seeding and OPML mapping.</summary>
    public string Slug { get; set; } = "";

    public string Color { get; set; } = "#a83020";
    public int SortOrder { get; set; }

    public List<Subscription> Subscriptions { get; set; } = new();
}

/// <summary>
/// A globally-shared RSS/Atom feed, deduplicated by URL. One row regardless of how
/// many users subscribe; it owns the (shared) article rows.
/// </summary>
public sealed class FeedSource
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FeedUrl { get; set; } = "";
    public string? SiteUrl { get; set; }
    public string Title { get; set; } = "";

    /// <summary>One/two-letter glyph used for the source badge in the UI.</summary>
    public string IconText { get; set; } = "";

    public DateTimeOffset? LastFetchedAt { get; set; }
    public string? LastFetchError { get; set; }

    // Conditional-GET caching to be a polite RSS citizen.
    public string? ETag { get; set; }
    public string? LastModified { get; set; }

    public List<Article> Articles { get; set; } = new();
    public List<Subscription> Subscriptions { get; set; } = new();
}

/// <summary>A user's subscription to a shared <see cref="FeedSource"/>, filed into one category.</summary>
public sealed class Subscription
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public Guid SourceId { get; set; }
    public FeedSource? Source { get; set; }

    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>Optional per-user override; falls back to <see cref="FeedSource.Title"/>.</summary>
    public string? CustomTitle { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>A single shared article belonging to a <see cref="FeedSource"/>. Per-user
/// read/saved state lives in <see cref="UserArticleState"/>, not here.</summary>
public sealed class Article
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SourceId { get; set; }
    public FeedSource? Source { get; set; }

    /// <summary>The feed-provided guid/id, unique within a source; used for de-duplication.</summary>
    public string ExternalId { get; set; } = "";

    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string? Summary { get; set; }
    public string? ContentHtml { get; set; }
    public string Url { get; set; } = "";
    public string? ImageUrl { get; set; }

    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Calendar day this article belongs to (configured timezone). Drives editions.</summary>
    public DateOnly EditionDate { get; set; }

    public List<UserArticleState> States { get; set; } = new();
}

/// <summary>Per-user read/saved/position overlay on a shared <see cref="Article"/>.
/// Created lazily on first interaction; an absent row means unread/unsaved/position 0.</summary>
public sealed class UserArticleState
{
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public Guid ArticleId { get; set; }
    public Article? Article { get; set; }

    public bool IsRead { get; set; }
    public bool IsSaved { get; set; }
    public int ReadingPositionPercent { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A per-user mute term. Articles matching are hidden from editions.</summary>
public sealed class KeywordFilter
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Stored lower-cased for case-insensitive matching.</summary>
    public string Term { get; set; } = "";
    public KeywordScope Scope { get; set; } = KeywordScope.Everywhere;
}

/// <summary>A signed-in device/session. Powers the Sync &amp; devices screen + remote sign-out.</summary>
public sealed class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public string DeviceLabel { get; set; } = "Unknown device";
    public string UserAgent { get; set; } = "";
    public string? IpAddress { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
}
