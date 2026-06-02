using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Services;

/// <summary>Pre-generates daily/weekly digests for users who opted in, so they're ready to read
/// without a manual click. Runs once a day at 23:55 edition-local time: the daily briefing for the day
/// just ending, and — on Saturdays — The Weekly for the week ending that day (ready to read Sunday).
/// Each run is the period's final word, so it <b>overwrites</b> any digest generated earlier in that
/// period (a mid-day or mid-week manual one only saw part of it). Resilient: one user's LLM failure
/// never aborts the run.</summary>
public sealed class AiSummaryBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<FeedOptions> options,
    ILogger<AiSummaryBackgroundService> log) : PeriodicBackgroundService(log)
{
    private readonly FeedOptions _options = options.Value;

    /// <summary>When the nightly run fires, in the configured edition timezone — late enough that the
    /// day's stories are essentially all in.</summary>
    private static readonly TimeOnly RunAt = new(23, 55);

    protected override string Name => "AI nightly";
    // This worker runs at a fixed clock time, not a fixed cadence: both the first wait and the wait
    // between runs are "until the next 23:55", recomputed each loop.
    protected override TimeSpan InitialDelay => DelayUntilNextRun();
    protected override TimeSpan Period => DelayUntilNextRun();

    /// <summary>Time until the next <see cref="RunAt"/> in the edition timezone.</summary>
    private TimeSpan DelayUntilNextRun()
    {
        var tz = EditionClock.ResolveTimeZone(_options.EditionTimeZone);
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        var todayRun = new DateTimeOffset(
            nowLocal.Year, nowLocal.Month, nowLocal.Day, RunAt.Hour, RunAt.Minute, 0, nowLocal.Offset);
        var next = nowLocal < todayRun ? todayRun : todayRun.AddDays(1);
        var delay = next - nowLocal;
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromMinutes(1);
    }

    protected override async Task RunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ai = scope.ServiceProvider.GetRequiredService<AiSummaryService>();

        var users = await db.Users
            .Where(u => u.AiEnabled && u.AiApiKeyEncrypted != null && (u.AiAutoDaily || u.AiAutoWeekly))
            .AsTracking()
            .ToListAsync(ct);
        if (users.Count == 0) return;

        var today = EditionClock.Today(_options);
        var isSaturday = today.DayOfWeek == DayOfWeek.Saturday;
        // On Saturday night the Sunday–Saturday week is complete; curate it so it's ready Sunday morning.
        var (weekStart, weekEnd) = AiSummaryService.WeeklyWindow(today);

        foreach (var user in users)
        {
            ct.ThrowIfCancellationRequested();
            if (user.AiAutoDaily)
                await RefreshDailyAsync(ai, user, today, ct);
            if (isSaturday && user.AiAutoWeekly)
                await RefreshWeeklyAsync(ai, user, weekStart, weekEnd, ct);
        }
    }

    /// <summary>Generates the day's daily briefing at end of day, overwriting any copy made earlier that day —
    /// the nightly run is the day's final word, finalising a mid-day briefing that only saw part of the day.</summary>
    private async Task RefreshDailyAsync(AiSummaryService ai, AppUser user, DateOnly day, CancellationToken ct)
    {
        try
        {
            await ai.GenerateAsync(user, AiSummaryKind.Daily, day, day, ct, AiJobTrigger.Scheduled);
            Log.LogInformation("Generated end-of-day daily summary for user {UserId}", user.Id);
        }
        catch (AiException ex) { Log.LogInformation("Skipped daily summary for user {UserId}: {Reason}", user.Id, ex.Message); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Log.LogWarning(ex, "Failed to generate daily summary for user {UserId}", user.Id); }
    }

    /// <summary>Writes The Weekly on Saturday night, overwriting any copy made earlier in the week — the
    /// Saturday run is the week's final word, finalising a mid-week manual one that only saw part of it.</summary>
    private async Task RefreshWeeklyAsync(AiSummaryService ai, AppUser user, DateOnly start, DateOnly end, CancellationToken ct)
    {
        try
        {
            await ai.GenerateAsync(user, AiSummaryKind.Weekly, start, end, ct, AiJobTrigger.Scheduled);
            Log.LogInformation("Generated end-of-week Weekly for user {UserId}", user.Id);
        }
        catch (AiException ex) { Log.LogInformation("Skipped The Weekly for user {UserId}: {Reason}", user.Id, ex.Message); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Log.LogWarning(ex, "Failed to generate The Weekly for user {UserId}", user.Id); }
    }
}
