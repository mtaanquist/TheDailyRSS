using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Services;

/// <summary>Pre-generates daily/weekly digests for users who opted in, so they're ready to read
/// without a manual click. Resilient: one user's LLM failure never aborts the sweep.</summary>
public sealed class AiSummaryBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<FeedOptions> options,
    ILogger<AiSummaryBackgroundService> log) : BackgroundService
{
    private readonly FeedOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        // Sweep a few times a day so freshly-completed days/weeks get picked up promptly.
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log.LogError(ex, "AI summary sweep failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
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
        var yesterday = today.AddDays(-1);
        // "The Weekly" covers the week ending on the most recent Sunday; ensuring it each sweep means
        // the new edition is curated on the first sweep on/after Sunday morning and then stands all week.
        var (weekStart, weekEnd) = AiSummaryService.WeeklyWindow(today);

        foreach (var user in users)
        {
            ct.ThrowIfCancellationRequested();
            if (user.AiAutoDaily)
                await EnsureDailyAsync(ai, user, yesterday, ct);
            if (user.AiAutoWeekly)
                await EnsureWeeklyAsync(ai, user, weekStart, weekEnd, ct);
        }
    }

    private async Task EnsureDailyAsync(AiSummaryService ai, AppUser user, DateOnly day, CancellationToken ct)
    {
        try
        {
            if (await ai.GetCachedAsync(user.Id, AiSummaryKind.Daily, day, day, ct) is not null) return;
            await ai.GenerateAsync(user, AiSummaryKind.Daily, day, day, ct);
            log.LogInformation("Pre-generated daily summary for user {UserId}", user.Id);
        }
        catch (AiException ex) { log.LogInformation("Skipped daily summary for user {UserId}: {Reason}", user.Id, ex.Message); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Failed to pre-generate daily summary for user {UserId}", user.Id); }
    }

    private async Task EnsureWeeklyAsync(AiSummaryService ai, AppUser user, DateOnly start, DateOnly end, CancellationToken ct)
    {
        try
        {
            // A null edition means it's uncurated (or a legacy markdown row) — (re)curate into the new format.
            if (await ai.GetWeeklyEditionAsync(user.Id, start, end, ct) is not null) return;
            await ai.GenerateWeeklyEditionAsync(user, start, end, ct);
            log.LogInformation("Curated The Weekly for user {UserId}", user.Id);
        }
        catch (AiException ex) { log.LogInformation("Skipped The Weekly for user {UserId}: {Reason}", user.Id, ex.Message); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Failed to curate The Weekly for user {UserId}", user.Id); }
    }
}
