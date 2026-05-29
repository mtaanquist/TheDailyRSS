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
        var (weekStart, weekEnd) = AiSummaryService.WeekRange(today.AddDays(-7));

        foreach (var user in users)
        {
            ct.ThrowIfCancellationRequested();
            if (user.AiAutoDaily)
                await EnsureAsync(ai, user, AiSummaryKind.Daily, yesterday, yesterday, ct);
            if (user.AiAutoWeekly)
                await EnsureAsync(ai, user, AiSummaryKind.Weekly, weekStart, weekEnd, ct);
        }
    }

    private async Task EnsureAsync(
        AiSummaryService ai, AppUser user, AiSummaryKind kind, DateOnly start, DateOnly end, CancellationToken ct)
    {
        try
        {
            if (await ai.GetCachedAsync(user.Id, kind, start, end, ct) is not null) return;
            await ai.GenerateAsync(user, kind, start, end, ct);
            log.LogInformation("Pre-generated {Kind} summary for user {UserId}", kind, user.Id);
        }
        catch (AiException ex)
        {
            log.LogInformation("Skipped {Kind} summary for user {UserId}: {Reason}", kind, user.Id, ex.Message);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to pre-generate {Kind} summary for user {UserId}", kind, user.Id);
        }
    }
}
