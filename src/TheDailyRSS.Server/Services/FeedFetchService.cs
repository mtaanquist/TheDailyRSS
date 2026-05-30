using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheDailyRSS.Server.Data;

namespace TheDailyRSS.Server.Services;

/// <summary>Refreshes a single source: conditional fetch, parse, upsert articles, prune.</summary>
public sealed class FeedFetchService(
    AppDbContext db,
    FeedReader reader,
    IHttpClientFactory httpFactory,
    ArticleContentExtractor extractor,
    IOptions<FeedOptions> options,
    ILogger<FeedFetchService> log)
{
    private readonly FeedOptions _options = options.Value;

    public async Task<int> RefreshAsync(FeedSource source, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("feeds");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, source.FeedUrl);
            if (!string.IsNullOrEmpty(source.ETag))
                req.Headers.TryAddWithoutValidation("If-None-Match", source.ETag);
            if (!string.IsNullOrEmpty(source.LastModified))
                req.Headers.TryAddWithoutValidation("If-Modified-Since", source.LastModified);

            using var resp = await http.SendAsync(req, ct);
            source.LastFetchedAt = DateTimeOffset.UtcNow;

            if (resp.StatusCode == HttpStatusCode.NotModified)
            {
                source.LastFetchError = null;
                await db.SaveChangesAsync(ct);
                return 0;
            }

            resp.EnsureSuccessStatusCode();
            source.ETag = resp.Headers.ETag?.ToString();
            source.LastModified = resp.Content.Headers.LastModified?.ToString("R");

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var buffered = await HttpStreamUtil.ReadCappedAsync(stream, _options.MaxResponseBytes, ct);
            buffered.Position = 0;

            var parsed = reader.Parse(buffered, source.FeedUrl);
            var added = await UpsertAsync(source, parsed, ct);

            if (source.SiteUrl is null && parsed.SiteUrl is not null)
                source.SiteUrl = parsed.SiteUrl;

            source.LastFetchError = null;
            await db.SaveChangesAsync(ct);
            await PruneAsync(source.Id, ct);
            return added;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to refresh source {SourceId} ({Url})", source.Id, source.FeedUrl);
            // Drop any half-applied article inserts so recording the error can't fail on the same data.
            db.ChangeTracker.Clear();
            try
            {
                var tracked = await db.FeedSources.FirstOrDefaultAsync(s => s.Id == source.Id, ct);
                if (tracked is not null)
                {
                    tracked.LastFetchedAt = DateTimeOffset.UtcNow;
                    tracked.LastFetchError = ex.Message;
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception saveEx)
            {
                log.LogWarning(saveEx, "Could not record fetch error for source {SourceId}", source.Id);
            }
            return 0;
        }
    }

    private async Task<int> UpsertAsync(FeedSource source, ParsedFeed parsed, CancellationToken ct)
    {
        var existing = await db.Articles
            .Where(a => a.SourceId == source.Id)
            .Select(a => a.ExternalId)
            .ToHashSetAsync(ct);

        var added = 0;

        // For full-content sources, extract reader-mode bodies inline only for the newest few new
        // items (so a synchronous Add stays fast); the backfill worker fills in the remainder.
        var newItems = parsed.Items.Where(i => existing.Add(i.ExternalId)).ToList();
        var inlineExtractUntil = source.FetchFullContent
            ? newItems
                .OrderByDescending(i => i.PublishedAt)
                .Take(Math.Max(0, _options.FullContentInlineLimit))
                .Select(i => i.ExternalId)
                .ToHashSet()
            : [];

        foreach (var item in newItems)
        {
            var article = new Article
            {
                SourceId = source.Id,
                ExternalId = item.ExternalId.Truncate(1000),
                Title = item.Title.Truncate(1000),
                Author = item.Author.Truncate(300),
                Summary = item.Summary,
                ContentHtml = item.ContentHtml,
                Url = item.Url.Truncate(2000),
                ImageUrl = item.ImageUrl.Truncate(2000),
                // Npgsql's timestamptz only accepts UTC offsets; feeds may publish in local time.
                PublishedAt = item.PublishedAt.ToUniversalTime(),
                FetchedAt = DateTimeOffset.UtcNow,
                EditionDate = EditionClock.EditionDate(item.PublishedAt, _options.EditionTimeZone),
                Fields = item.Fields,
            };

            if (inlineExtractUntil.Contains(item.ExternalId) && !string.IsNullOrWhiteSpace(item.Url))
            {
                // Never let an extraction failure bubble out — it would reach RefreshAsync's catch,
                // which clears the ChangeTracker and discards every pending insert in this sweep.
                try
                {
                    article.FullContentHtml = await extractor.ExtractAsync(item.Url, ct) ?? "";
                }
                catch (Exception ex)
                {
                    log.LogDebug(ex, "Inline full-content extraction failed for {Url}", item.Url);
                }
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _options.FullContentDelaySeconds)), ct);
            }

            db.Articles.Add(article);
            added++;
        }
        return added;
    }

    private async Task PruneAsync(Guid sourceId, CancellationToken ct)
    {
        if (_options.MaxArticlesPerFeed <= 0) return;

        var keepIds = await db.Articles
            .Where(a => a.SourceId == sourceId)
            .OrderByDescending(a => a.PublishedAt)
            .Select(a => a.Id)
            .Take(_options.MaxArticlesPerFeed)
            .ToListAsync(ct);

        // Never prune an article that ANY user has saved.
        await db.Articles
            .Where(a => a.SourceId == sourceId
                && !keepIds.Contains(a.Id)
                && !db.UserArticleStates.Any(s => s.ArticleId == a.Id && s.IsSaved))
            .ExecuteDeleteAsync(ct);
    }
}
