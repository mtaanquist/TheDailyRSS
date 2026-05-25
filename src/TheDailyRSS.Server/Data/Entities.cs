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

    public List<Category> Categories { get; set; } = new();
    public List<Feed> Feeds { get; set; } = new();
    public List<UserSession> Sessions { get; set; } = new();
}

/// <summary>A folder of feeds (a "section" of the newspaper).</summary>
public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public string Name { get; set; } = "";
    public string Color { get; set; } = "#a83020";
    public int SortOrder { get; set; }

    public List<Feed> Feeds { get; set; } = new();
}

/// <summary>A subscribed RSS/Atom feed.</summary>
public sealed class Feed
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }

    public string Title { get; set; } = "";
    public string FeedUrl { get; set; } = "";
    public string? SiteUrl { get; set; }

    /// <summary>One/two-letter glyph used for the source badge in the UI.</summary>
    public string IconText { get; set; } = "";
    public int SortOrder { get; set; }

    public DateTimeOffset? LastFetchedAt { get; set; }
    public string? LastFetchError { get; set; }

    // Conditional-GET caching to be a polite RSS citizen.
    public string? ETag { get; set; }
    public string? LastModified { get; set; }

    public List<Article> Articles { get; set; } = new();
}

/// <summary>A single article. Read/saved state lives here because feeds are per-user.</summary>
public sealed class Article
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FeedId { get; set; }
    public Feed? Feed { get; set; }

    /// <summary>The feed-provided guid/id, unique within a feed; used for de-duplication.</summary>
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

    public bool IsRead { get; set; }
    public bool IsSaved { get; set; }
    public int ReadingPositionPercent { get; set; }
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
