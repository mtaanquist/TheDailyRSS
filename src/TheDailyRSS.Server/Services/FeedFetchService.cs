using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheDailyRSS.Server.Data;

namespace TheDailyRSS.Server.Services;

/// <summary>Refreshes a single feed: conditional fetch, parse, upsert articles, prune.</summary>
public sealed class FeedFetchService(
    AppDbContext db,
    FeedReader reader,
    IHttpClientFactory httpFactory,
    IOptions<FeedOptions> options,
    ILogger<FeedFetchService> log)
{
    private readonly FeedOptions _options = options.Value;

    public async Task<int> RefreshAsync(Feed feed, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("feeds");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, feed.FeedUrl);
            if (!string.IsNullOrEmpty(feed.ETag))
                req.Headers.TryAddWithoutValidation("If-None-Match", feed.ETag);
            if (!string.IsNullOrEmpty(feed.LastModified))
                req.Headers.TryAddWithoutValidation("If-Modified-Since", feed.LastModified);

            using var resp = await http.SendAsync(req, ct);
            feed.LastFetchedAt = DateTimeOffset.UtcNow;

            if (resp.StatusCode == HttpStatusCode.NotModified)
            {
                feed.LastFetchError = null;
                await db.SaveChangesAsync(ct);
                return 0;
            }

            resp.EnsureSuccessStatusCode();
            feed.ETag = resp.Headers.ETag?.ToString();
            feed.LastModified = resp.Content.Headers.LastModified?.ToString("R");

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var buffered = new MemoryStream();
            await stream.CopyToAsync(buffered, ct);
            buffered.Position = 0;

            var parsed = reader.Parse(buffered, feed.FeedUrl);
            var added = await UpsertAsync(feed, parsed, ct);

            if (feed.SiteUrl is null && parsed.SiteUrl is not null)
                feed.SiteUrl = parsed.SiteUrl;

            feed.LastFetchError = null;
            await db.SaveChangesAsync(ct);
            await PruneAsync(feed.Id, ct);
            return added;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to refresh feed {FeedId} ({Url})", feed.Id, feed.FeedUrl);
            feed.LastFetchedAt = DateTimeOffset.UtcNow;
            feed.LastFetchError = ex.Message;
            await db.SaveChangesAsync(ct);
            return 0;
        }
    }

    private async Task<int> UpsertAsync(Feed feed, ParsedFeed parsed, CancellationToken ct)
    {
        var existing = await db.Articles
            .Where(a => a.FeedId == feed.Id)
            .Select(a => a.ExternalId)
            .ToHashSetAsync(ct);

        var tz = ResolveTimeZone(_options.EditionTimeZone);
        var added = 0;

        foreach (var item in parsed.Items)
        {
            if (!existing.Add(item.ExternalId))
                continue;

            var local = TimeZoneInfo.ConvertTime(item.PublishedAt, tz);
            db.Articles.Add(new Article
            {
                FeedId = feed.Id,
                ExternalId = Trim(item.ExternalId, 1000),
                Title = Trim(item.Title, 1000),
                Author = Trim(item.Author, 300),
                Summary = item.Summary,
                ContentHtml = item.ContentHtml,
                Url = Trim(item.Url, 2000),
                ImageUrl = Trim(item.ImageUrl, 2000),
                PublishedAt = item.PublishedAt,
                FetchedAt = DateTimeOffset.UtcNow,
                EditionDate = DateOnly.FromDateTime(local.DateTime),
            });
            added++;
        }
        return added;
    }

    private async Task PruneAsync(Guid feedId, CancellationToken ct)
    {
        if (_options.MaxArticlesPerFeed <= 0) return;

        var keepIds = await db.Articles
            .Where(a => a.FeedId == feedId)
            .OrderByDescending(a => a.PublishedAt)
            .Select(a => a.Id)
            .Take(_options.MaxArticlesPerFeed)
            .ToListAsync(ct);

        // Never prune saved articles.
        await db.Articles
            .Where(a => a.FeedId == feedId && !a.IsSaved && !keepIds.Contains(a.Id))
            .ExecuteDeleteAsync(ct);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Utc; }
    }

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(value))]
    private static string? Trim(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
