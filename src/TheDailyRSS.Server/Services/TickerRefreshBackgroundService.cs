using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheDailyRSS.Server.Data;

namespace TheDailyRSS.Server.Services;

/// <summary>Refreshes the latest quote for every symbol at least one reader tracks. Fetches once per symbol
/// per sweep — not per reader — and updates the shared <see cref="Ticker"/> row, so the front-page bar and
/// the tickers page stay current without each reader triggering their own calls.</summary>
public sealed class TickerRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    TickerService tickers,
    IOptions<FeedOptions> options,
    ILogger<TickerRefreshBackgroundService> log) : PeriodicBackgroundService(log)
{
    private readonly FeedOptions _options = options.Value;

    /// <summary>Be polite to the free API between symbols.</summary>
    private static readonly TimeSpan BetweenSymbols = TimeSpan.FromMilliseconds(300);

    protected override string Name => "Tickers";
    protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(20);
    protected override TimeSpan Period => TimeSpan.FromMinutes(Math.Max(1, _options.TickerRefreshIntervalMinutes));

    protected override async Task RunAsync(CancellationToken ct)
    {
        List<string> symbols;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            symbols = await db.UserTickers.Select(x => x.Symbol).Distinct().ToListAsync(ct);
        }
        if (symbols.Count == 0) return;

        foreach (var symbol in symbols)
        {
            ct.ThrowIfCancellationRequested();
            var quote = await tickers.FetchQuoteAsync(symbol, ct);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Tickers.FirstOrDefaultAsync(t => t.Symbol == symbol, ct);
            if (row is null) continue; // removed between the listing and now

            if (quote is null)
            {
                // Keep the last good price; just note the miss so the UI can show staleness if it wants.
                row.LastError = "Quote unavailable at last refresh.";
            }
            else
            {
                TickerService.Apply(row, quote);
            }
            await db.SaveChangesAsync(ct);

            await Task.Delay(BetweenSymbols, ct);
        }
    }
}
