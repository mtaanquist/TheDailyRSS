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
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fetcher = scope.ServiceProvider.GetRequiredService<FeedFetchService>();

        var sources = await db.FeedSources.AsTracking().ToListAsync(ct);
        log.LogInformation("Refreshing {Count} sources", sources.Count);

        var total = 0;
        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            total += await fetcher.RefreshAsync(source, ct);
        }

        if (total > 0)
            log.LogInformation("Fetched {Count} new articles", total);
    }
}
