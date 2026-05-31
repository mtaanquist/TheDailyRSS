using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Services;

/// <summary>Pre-generates daily/weekly digests for users who opted in, so they're ready to read
/// without a manual click. Runs once a day at 23:55 edition-local time: the daily briefing for the day
/// just ending, and — on Saturdays — The Weekly for the week ending that day (ready to read Sunday).
/// Resilient: one user's LLM failure never aborts the run.</summary>
public sealed class AiSummaryBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<FeedOptions> options,
    ILogger<AiSummaryBackgroundService> log) : BackgroundService
{
    private readonly FeedOptions _options = options.Value;

    /// <summary>When the nightly run fires, in the configured edition timezone — late enough that the
    /// day's stories are essentially all in.</summary>
    private static readonly TimeOnly RunAt = new(23, 55);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(DelayUntilNextRun(), stoppingToken); }
            catch (OperationCanceledException) { return; }

            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log.LogError(ex, "AI nightly run failed");
            }
        }
    }

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

    private async Task RunAsync(CancellationToken ct)
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
                await EnsureDailyAsync(ai, user, today, ct);
            if (isSaturday && user.AiAutoWeekly)
                await EnsureWeeklyAsync(ai, user, weekStart, weekEnd, ct);
        }
    }

    private async Task EnsureDailyAsync(AiSummaryService ai, AppUser user, DateOnly day, CancellationToken ct)
    {
        try
        {
            if (await ai.GetCachedAsync(user.Id, AiSummaryKind.Daily, day, day, ct) is not null) return;
            await ai.GenerateAsync(user, AiSummaryKind.Daily, day, day, ct, AiJobTrigger.Scheduled);
            log.LogInformation("Pre-generated daily summary for user {UserId}", user.Id);
        }
        catch (AiException ex) { log.LogInformation("Skipped daily summary for user {UserId}: {Reason}", user.Id, ex.Message); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Failed to pre-generate daily summary for user {UserId}", user.Id); }
    }

    private async Task EnsureWeeklyAsync(AiSummaryService ai, AppUser user, DateOnly start, DateOnly end, CancellationToken ct)
    {
        try
        {
            if (await ai.GetCachedAsync(user.Id, AiSummaryKind.Weekly, start, end, ct) is not null) return;
            await ai.GenerateAsync(user, AiSummaryKind.Weekly, start, end, ct, AiJobTrigger.Scheduled);
            log.LogInformation("Generated The Weekly for user {UserId}", user.Id);
        }
        catch (AiException ex) { log.LogInformation("Skipped The Weekly for user {UserId}: {Reason}", user.Id, ex.Message); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Failed to generate The Weekly for user {UserId}", user.Id); }
    }
}
