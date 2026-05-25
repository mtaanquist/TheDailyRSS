using Microsoft.EntityFrameworkCore;
using TheDailyRSS.Server.Data;

namespace TheDailyRSS.Server.Services;

/// <summary>
/// Resolves the single, globally-shared <see cref="FeedSource"/> for a feed URL, creating
/// it once and reusing it for every subsequent subscriber (article de-duplication).
/// </summary>
public sealed class FeedSourceService(AppDbContext db)
{
    /// <summary>
    /// Returns the source for <paramref name="feedUrl"/>, creating it if needed. The
    /// <c>Created</c> flag is true only when this call inserted the row (so the caller can
    /// trigger an immediate first fetch). Survives the concurrent-first-subscriber race via
    /// the unique index on <see cref="FeedSource.FeedUrl"/>.
    /// </summary>
    public async Task<(FeedSource Source, bool Created)> GetOrCreateAsync(
        string feedUrl, string title, string? siteUrl, CancellationToken ct)
    {
        var normalized = NormalizeUrl(feedUrl);

        var existing = await db.FeedSources.FirstOrDefaultAsync(s => s.FeedUrl == normalized, ct);
        if (existing is not null) return (existing, false);

        var source = new FeedSource
        {
            FeedUrl = normalized,
            Title = Trim(title, 300),
            SiteUrl = Trim(siteUrl, 2000),
            IconText = IconText.From(title),
        };
        db.FeedSources.Add(source);
        try
        {
            await db.SaveChangesAsync(ct);
            return (source, true);
        }
        catch (DbUpdateException)
        {
            // Lost the race: someone else created this source. Detach ours and reuse theirs.
            db.Entry(source).State = EntityState.Detached;
            var winner = await db.FeedSources.FirstOrDefaultAsync(s => s.FeedUrl == normalized, ct);
            if (winner is null) throw;
            return (winner, false);
        }
    }

    /// <summary>Lower-cases scheme/host and strips default ports so trivial URL variants dedupe.</summary>
    public static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return trimmed;

        var b = new UriBuilder(uri)
        {
            Host = uri.Host.ToLowerInvariant(),
            Scheme = uri.Scheme.ToLowerInvariant(),
        };
        if ((b.Scheme == "http" && b.Port == 80) || (b.Scheme == "https" && b.Port == 443))
            b.Port = -1;
        return b.Uri.ToString();
    }

    private static string Trim(string? value, int max) =>
        value is null ? "" : value.Length <= max ? value : value[..max];
}
