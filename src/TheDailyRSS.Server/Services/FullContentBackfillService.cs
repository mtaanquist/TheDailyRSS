using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheDailyRSS.Server.Data;

namespace TheDailyRSS.Server.Services;

/// <summary>
/// Polite background worker that fills in reader-mode <see cref="Article.FullContentHtml"/> for
/// articles already stored under a source whose <see cref="FeedSource.FetchFullContent"/> is on.
/// DB-scan driven (restart-safe: the pending set is simply "articles with null FullContentHtml under
/// a full-content source"), newest-first, with a per-host delay between fetches so we don't get
/// blocked for abuse. Slow is acceptable.
/// </summary>
public sealed class FullContentBackfillService(
    IServiceScopeFactory scopeFactory,
    ArticleContentExtractor extractor,
    IOptions<FeedOptions> options,
    ILogger<FullContentBackfillService> log) : PeriodicBackgroundService(log)
{
    private readonly FeedOptions _options = options.Value;

    protected override string Name => "Full-content backfill";
    // Let the DB/migrations settle, and stagger after the feed-refresh service's own startup delay.
    protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(20);
    protected override TimeSpan Period => TimeSpan.FromMinutes(Math.Max(1, _options.FullContentBackfillIntervalMinutes));

    protected override async Task RunAsync(CancellationToken ct)
    {
        List<Guid> sourceIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            sourceIds = await db.FeedSources
                .Where(s => s.FetchFullContent
                    && s.Articles.Any(a => a.FullContentHtml == null))
                .Select(s => s.Id)
                .ToListAsync(ct);
        }
        if (sourceIds.Count == 0) return;

        var delay = TimeSpan.FromSeconds(Math.Max(0, _options.FullContentDelaySeconds));
        var batchSize = Math.Max(1, _options.FullContentBackfillBatchSize);

        // One source at a time, serially, so the inter-request delay rate-limits per host. A fresh
        // scope per source keeps change-tracking isolated, mirroring the feed-refresh service.
        foreach (var id in sourceIds)
        {
            ct.ThrowIfCancellationRequested();
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var pending = await db.Articles
                .Where(a => a.SourceId == id && a.FullContentHtml == null)
                .OrderByDescending(a => a.PublishedAt) // newest first
                .Take(batchSize)
                .Select(a => new { a.Id, a.Url })
                .ToListAsync(ct);

            foreach (var a in pending)
            {
                // "" sentinel = attempted, nothing usable → don't retry next sweep; fall back to feed
                // content. Also stamped on URL-less rows so they don't keep the source forever pending.
                var extracted = string.IsNullOrWhiteSpace(a.Url) ? "" : await extractor.ExtractAsync(a.Url, ct) ?? "";
                await db.Articles.Where(x => x.Id == a.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.FullContentHtml, extracted), ct);

                if (!string.IsNullOrWhiteSpace(a.Url))
                    await Task.Delay(delay, ct);
            }
        }
    }
}
