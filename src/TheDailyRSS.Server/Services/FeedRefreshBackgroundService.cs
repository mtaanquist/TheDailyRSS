using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheDailyRSS.Server.Data;

namespace TheDailyRSS.Server.Services;

/// <summary>Periodically refreshes every shared feed source (once each, regardless of subscribers).</summary>
public sealed class FeedRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<FeedOptions> options,
    ILogger<FeedRefreshBackgroundService> log) : PeriodicBackgroundService(log)
{
    private readonly FeedOptions _options = options.Value;

    protected override string Name => "Feed refresh";
    // Small delay so the DB/migrations are ready before the first sweep.
    protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(10);
    protected override TimeSpan Period => TimeSpan.FromMinutes(Math.Max(1, _options.RefreshIntervalMinutes));

    protected override async Task RunAsync(CancellationToken ct)
    {
        List<Guid> sourceIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            sourceIds = await db.FeedSources.Select(s => s.Id).ToListAsync(ct);
        }
        Log.LogInformation("Refreshing {Count} sources", sourceIds.Count);

        // A fresh scope (DbContext + FeedFetchService) per source isolates change-tracking and error
        // handling: one source's failure (and the ChangeTracker.Clear it triggers) can't discard
        // pending work for the others, and the context doesn't accumulate every source's entities.
        var total = 0;
        foreach (var id in sourceIds)
        {
            ct.ThrowIfCancellationRequested();
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fetcher = scope.ServiceProvider.GetRequiredService<FeedFetchService>();

            var source = await db.FeedSources.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (source is null) continue; // deleted since the id list was taken
            total += await fetcher.RefreshAsync(source, ct);
        }

        if (total > 0)
            Log.LogInformation("Fetched {Count} new articles", total);
    }
}
