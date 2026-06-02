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

    /// <summary>The reader's sticky "unread only" filter for the front page / editions. Persisted (not a
    /// per-navigation flag) so it survives moving between sections, opening an article, and reloads (#  reading).</summary>
    public bool UnreadOnly { get; set; }

    /// <summary>"No pictures" mode: don't render any images for this reader. Images are still fetched and
    /// stored, so re-enabling brings them straight back (issue #41).</summary>
    public bool HideImages { get; set; }

    // ── Bring-your-own-key AI summaries (opt-in, per-user) ──────────────
    /// <summary>Master opt-in. When false, no AI affordances appear and no summaries are generated.</summary>
    public bool AiEnabled { get; set; }

    /// <summary>OpenAI-compatible base URL, e.g. <c>https://api.openai.com/v1</c>.</summary>
    public string? AiBaseUrl { get; set; }

    /// <summary>Chat-completions model id, e.g. <c>gpt-4o-mini</c>.</summary>
    public string? AiModel { get; set; }

    /// <summary>The user's API key, encrypted at rest via DataProtection. Never leaves the server.</summary>
    public string? AiApiKeyEncrypted { get; set; }

    /// <summary>Free-text description of the reader's interests; steers every digest.</summary>
    public string? AiSystemPrompt { get; set; }

    /// <summary>Pre-generate the previous day's digest in the background.</summary>
    public bool AiAutoDaily { get; set; }

    /// <summary>Pre-generate the previous week's digest in the background.</summary>
    public bool AiAutoWeekly { get; set; }

    /// <summary>Pre-generate a per-article TL;DR in the background for articles from full-text feeds.</summary>
    public bool AiAutoArticle { get; set; }

    // ── Local weather (opt-in, per-user; issue #33) ─────────────────────
    /// <summary>Show the at-a-glance weather in the masthead.</summary>
    public bool ShowWeather { get; set; }

    /// <summary>Human-readable geocoded label, e.g. "Copenhagen, Denmark". Null when unset.</summary>
    public string? WeatherLocationName { get; set; }

    /// <summary>Geocoded coordinates for the saved location; both null when unset. The forecast is keyed by
    /// these (rounded) so readers in the same place share one stored snapshot.</summary>
    public double? WeatherLatitude { get; set; }
    public double? WeatherLongitude { get; set; }

    public List<Subscription> Subscriptions { get; set; } = new();
    public List<UserArticleState> ArticleStates { get; set; } = new();
    public List<KeywordFilter> KeywordFilters { get; set; } = new();
    public List<FieldFilter> FieldFilters { get; set; } = new();
    public List<UserSession> Sessions { get; set; } = new();
    public List<AiSummary> AiSummaries { get; set; } = new();
    public List<ArticleSummary> ArticleSummaries { get; set; } = new();
    public List<UserTicker> UserTickers { get; set; } = new();
    public List<RecoveryCode> RecoveryCodes { get; set; } = new();
    public List<UserCredential> Credentials { get; set; } = new();

    // ── Two-factor authentication (TOTP; opt-in, per-user; #38) ─────────
    /// <summary>When true, login requires a valid authenticator (or recovery) code after the password.</summary>
    public bool IsTotpEnabled { get; set; }

    /// <summary>The TOTP shared secret (base32), encrypted at rest via DataProtection. Set when enrollment
    /// begins; <see cref="IsTotpEnabled"/> only flips once a generated code is confirmed.</summary>
    public string? TotpSecretEncrypted { get; set; }
}

/// <summary>A registered WebAuthn/FIDO2 passkey for a reader (#38). Passkeys are a passwordless login
/// alternative — a full credential, not a second factor — so signing in with one skips TOTP. Stores the
/// public key + signature counter the spec needs to verify each assertion and detect cloned authenticators.</summary>
public sealed class UserCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>The authenticator's credential id (raw bytes); unique, used to look the credential up at login.</summary>
    public byte[] CredentialId { get; set; } = Array.Empty<byte>();

    /// <summary>COSE public key bytes, used to verify assertion signatures.</summary>
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();

    /// <summary>Signature counter; must strictly increase across assertions (cloned-key detection). Stored as
    /// long because Postgres has no unsigned types; cast to/from the spec's uint at the library boundary.</summary>
    public long SignCount { get; set; }

    /// <summary>Authenticator model identifier (AAGUID), captured at registration.</summary>
    public Guid AaGuid { get; set; }

    /// <summary>Reader-supplied label so multiple passkeys are distinguishable.</summary>
    public string Nickname { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary>A single-use backup code for signing in when an authenticator is unavailable. Stored hashed
/// (never plaintext); the plaintext set is shown to the reader exactly once, at enable time.</summary>
public sealed class RecoveryCode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Hex SHA-256 of the normalized (lower-cased, dash-stripped) code.</summary>
    public string CodeHash { get; set; } = "";

    /// <summary>Set when the code is redeemed; a non-null value means it can't be used again.</summary>
    public DateTimeOffset? UsedAt { get; set; }
}

/// <summary>A cached AI-generated digest for one user over a date range. Daily summaries set
/// <see cref="PeriodStart"/> == <see cref="PeriodEnd"/>; weekly summaries span seven days.</summary>
public sealed class AiSummary
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public AiSummaryKind Kind { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    public string Content { get; set; } = "";
    public string Model { get; set; } = "";
    public int ArticleCount { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A day's stored weather for one (rounded) location, keyed by edition date. Shared across all
/// readers in that location — like a <see cref="FeedSource"/>, not a per-user overlay — so a place is
/// fetched once per day regardless of how many readers watch it. Once the edition day passes the snapshot
/// is frozen, so paging back to an older edition shows the forecast that was on file for that day.</summary>
public sealed class WeatherSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Rounded "lat,lon" dedup key (see <c>WeatherService.LocationKey</c>).</summary>
    public string LocationKey { get; set; } = "";

    /// <summary>Calendar day this snapshot is for, in the configured edition timezone.</summary>
    public DateOnly EditionDate { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>The location-local IANA timezone the API reported, used to render hour labels.</summary>
    public string TimeZone { get; set; } = "";

    /// <summary>Conditions at the most recent fetch (meaningful for "today"; the last reading of the day
    /// for frozen past snapshots).</summary>
    public double CurrentTempC { get; set; }
    public int CurrentCode { get; set; }

    /// <summary>The day's high/low across the hourly series.</summary>
    public double HighTempC { get; set; }
    public double LowTempC { get; set; }

    /// <summary>The hourly series as a serialized <c>List&lt;HourlyWeatherDto&gt;</c> (Time/TempC/Code).</summary>
    public string HourlyJson { get; set; } = "";

    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A globally-shared stock/index quote, keyed by symbol and refreshed by a background worker
/// independent of any reader — like a <see cref="FeedSource"/>. Readers opt in via <see cref="UserTicker"/>;
/// the server fetches each watched symbol once per sweep regardless of how many readers track it (#32).</summary>
public sealed class Ticker
{
    /// <summary>Upper-cased symbol, e.g. "AAPL" or "^GSPC". Primary key.</summary>
    public string Symbol { get; set; } = "";

    public string Name { get; set; } = "";
    public string Currency { get; set; } = "";

    public double Price { get; set; }
    public double PreviousClose { get; set; }

    /// <summary>Exchange timestamp of the quote, when the source reports one.</summary>
    public DateTimeOffset? MarketTime { get; set; }

    /// <summary>When the worker last refreshed this row; null until the first successful fetch.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>The most recent fetch error, surfaced for diagnostics; null when healthy.</summary>
    public string? LastError { get; set; }

    public List<UserTicker> Watchers { get; set; } = new();
}

/// <summary>A reader's subscription to a shared <see cref="Ticker"/> — which symbols they track, and which
/// they've promoted to the front-page bar. Mirrors how <see cref="Subscription"/> overlays a feed source.</summary>
public sealed class UserTicker
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public string Symbol { get; set; } = "";
    public Ticker? Ticker { get; set; }

    /// <summary>Show this ticker in the front-page promoted bar.</summary>
    public bool Promoted { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>A recorded AI generation failure, for the admin error log. Denormalised (no FK to the user)
/// so the audit survives a user deletion and never cascades away. Bounded — the service prunes to the
/// most recent N on each insert.</summary>
public sealed class AiErrorLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The reader the job was for (email/identifier), captured at failure time.</summary>
    public string User { get; set; } = "";
    /// <summary>"Daily" / "Weekly" / "Article".</summary>
    public string Kind { get; set; } = "";
    /// <summary>"Scheduled" (nightly worker) or "Interactive" (a reader clicked generate).</summary>
    public string Trigger { get; set; } = "";
    /// <summary>The job's label (date, week range, or article title).</summary>
    public string? Label { get; set; }
    /// <summary>The raw error message, surfaced verbatim so the admin can read what actually went wrong.</summary>
    public string Message { get; set; } = "";
}

/// <summary>A global, admin-editable site setting (one row per key). Used for things like the AI
/// "house style" preamble. An absent or blank row means "fall back to the built-in default", so
/// no seed migration is needed.</summary>
public sealed class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Well-known <see cref="AppSetting.Key"/> values.</summary>
public static class SiteSettingKeys
{
    /// <summary>The admin-editable AI "house style" preamble shared by the daily briefing and The Weekly.</summary>
    public const string AiHouseStyle = "ai.house_style";
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

    /// <summary>When true, the fetcher steps into each article's URL and stores a reader-mode
    /// extraction in <see cref="Article.FullContentHtml"/>. Shared across all subscribers since
    /// the source (and its article rows) is global.</summary>
    public bool FetchFullContent { get; set; }

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

    /// <summary>Reader-mode extraction of the article page, populated only when the source has
    /// <see cref="FeedSource.FetchFullContent"/> on. <c>null</c> = not yet attempted;
    /// <c>""</c> = attempted but nothing usable (so it isn't retried). Served in preference to
    /// <see cref="ContentHtml"/> when present and non-empty.</summary>
    public string? FullContentHtml { get; set; }

    public string Url { get; set; } = "";
    public string? ImageUrl { get; set; }

    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Calendar day this article belongs to (configured timezone). Drives editions.</summary>
    public DateOnly EditionDate { get; set; }

    /// <summary>Structured fields captured from the original feed item (category, dc:creator,
    /// media:keywords, custom-namespace leaves). Stored as JSONB; keys and values are kept
    /// lower-cased so field filters can match via case-sensitive JSON containment.</summary>
    public Dictionary<string, List<string>> Fields { get; set; } = new();

    public List<UserArticleState> States { get; set; } = new();
    public List<ArticleSummary> Summaries { get; set; } = new();
}

/// <summary>A per-user AI TL;DR of a shared <see cref="Article"/>, generated with that user's own
/// BYOK endpoint. Per-user rather than shared because each reader brings their own model, key and
/// interests — mirroring how <see cref="UserArticleState"/> overlays a shared article.</summary>
public sealed class ArticleSummary
{
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public Guid ArticleId { get; set; }
    public Article? Article { get; set; }

    public string Content { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
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

    /// <summary>The reader dismissed this story; it's dropped from editions but listed in the Hidden view.</summary>
    public bool IsHidden { get; set; }

    public int ReadingPositionPercent { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A per-user mute term. Articles matching are hidden from editions.
/// Optionally scoped to a single <see cref="FeedSource"/> — null means every subscribed feed.</summary>
public sealed class KeywordFilter
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Stored lower-cased for case-insensitive matching.</summary>
    public string Term { get; set; } = "";
    public KeywordScope Scope { get; set; } = KeywordScope.Everywhere;

    /// <summary>When set, the rule only mutes articles from this feed.</summary>
    public Guid? SourceId { get; set; }
    public FeedSource? Source { get; set; }
}

/// <summary>A per-user mute rule that targets a single captured feed-item field/value pair
/// (e.g. <c>category = "guides"</c>). Optionally scoped to a single <see cref="FeedSource"/>
/// so muting "category=guides" on one tech feed doesn't drop history articles tagged
/// the same way on another.</summary>
public sealed class FieldFilter
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Stored lower-cased; matches the keys we put on <see cref="Article.Fields"/>.</summary>
    public string FieldKey { get; set; } = "";

    public FieldFilterOperator Operator { get; set; } = FieldFilterOperator.Equals;

    /// <summary>Stored lower-cased to match <see cref="Article.Fields"/> values via JSON containment.</summary>
    public string Value { get; set; } = "";

    /// <summary>When set, the rule only mutes articles from this feed; null means every feed.</summary>
    public Guid? SourceId { get; set; }
    public FeedSource? Source { get; set; }
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
