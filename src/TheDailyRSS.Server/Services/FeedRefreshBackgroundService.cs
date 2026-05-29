using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheDailyRSS.Server.Data;

namespace TheDailyRSS.Server.Services;

/// <summary>Periodically refreshes every shared feed source (once each, regardless of subscribers).</summary>
public sealed class FeedRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<FeedOptions> options,
    ILogger<FeedRefreshBackgroundService> log) : BackgroundService
{
    private readonly FeedOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay so the DB/migrations are ready before the first sweep.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.RefreshIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                await RefreshAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log.LogError(ex, "Feed refresh sweep failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshAllAsync(CancellationToken ct)
    {
        List<Guid> sourceIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            sourceIds = await db.FeedSources.Select(s => s.Id).ToListAsync(ct);
        }
        log.LogInformation("Refreshing {Count} sources", sourceIds.Count);

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
            log.LogInformation("Fetched {Count} new articles", total);
    }
}
