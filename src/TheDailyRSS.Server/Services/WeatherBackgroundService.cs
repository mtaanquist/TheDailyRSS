using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheDailyRSS.Server.Data;

namespace TheDailyRSS.Server.Services;

/// <summary>Keeps today's weather snapshot fresh for every distinct location readers have opted into.
/// Fetches once per (rounded) location per sweep — not per user — and only ever writes the current
/// edition day, so yesterday's snapshot stays frozen as that day's record.</summary>
public sealed class WeatherBackgroundService(
    IServiceScopeFactory scopeFactory,
    WeatherService weather,
    IOptions<FeedOptions> options,
    ILogger<WeatherBackgroundService> log) : PeriodicBackgroundService(log)
{
    private readonly FeedOptions _options = options.Value;

    /// <summary>Be polite to the free API between locations.</summary>
    private static readonly TimeSpan BetweenLocations = TimeSpan.FromMilliseconds(500);

    protected override string Name => "Weather";
    // Stagger after the feed workers' startup delays.
    protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(30);
    protected override TimeSpan Period => TimeSpan.FromMinutes(Math.Max(5, _options.WeatherRefreshIntervalMinutes));

    protected override async Task RunAsync(CancellationToken ct)
    {
        DateOnly today = EditionClock.Today(_options);

        List<(double Lat, double Lon)> locations;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.Users
                .Where(u => u.ShowWeather && u.WeatherLatitude != null && u.WeatherLongitude != null)
                .Select(u => new { u.WeatherLatitude, u.WeatherLongitude })
                .Distinct()
                .ToListAsync(ct);
            locations = rows.Select(r => (r.WeatherLatitude!.Value, r.WeatherLongitude!.Value)).ToList();
        }

        // Collapse to one fetch per stored key (nearby readers round to the same snapshot).
        var distinct = locations
            .GroupBy(l => WeatherService.LocationKey(l.Lat, l.Lon))
            .Select(g => g.First())
            .ToList();
        if (distinct.Count == 0) return;

        foreach (var (lat, lon) in distinct)
        {
            ct.ThrowIfCancellationRequested();
            var snap = await weather.FetchAsync(lat, lon, today, ct);
            if (snap is null) continue;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await UpsertAsync(db, snap, ct);

            await Task.Delay(BetweenLocations, ct);
        }
    }

    /// <summary>Inserts the snapshot, or refreshes the existing row for the same (location, day).</summary>
    private async Task UpsertAsync(AppDbContext db, WeatherSnapshot snap, CancellationToken ct)
    {
        var existing = await db.WeatherSnapshots
            .FirstOrDefaultAsync(s => s.LocationKey == snap.LocationKey && s.EditionDate == snap.EditionDate, ct);
        if (existing is null)
        {
            db.WeatherSnapshots.Add(snap);
        }
        else
        {
            existing.CurrentTempC = snap.CurrentTempC;
            existing.CurrentCode = snap.CurrentCode;
            existing.HighTempC = snap.HighTempC;
            existing.LowTempC = snap.LowTempC;
            existing.HourlyJson = snap.HourlyJson;
            existing.TimeZone = snap.TimeZone;
            existing.FetchedAt = snap.FetchedAt;
        }

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex)
        {
            // Raced with the on-demand fetch in the endpoint — harmless, the other write wins.
            Log.LogDebug(ex, "Weather upsert raced for {Key}", snap.LocationKey);
        }
    }
}
