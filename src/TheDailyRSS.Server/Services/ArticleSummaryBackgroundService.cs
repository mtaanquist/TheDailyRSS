using Microsoft.EntityFrameworkCore;
using TheDailyRSS.Server.Data;

namespace TheDailyRSS.Server.Services;

/// <summary>
/// Pre-generates per-article AI TL;DRs for users who opted in (<see cref="AppUser.AiAutoArticle"/>),
/// limited to articles from their full-text feeds — the only ones with enough body to summarise well.
/// DB-scan driven (restart-safe), newest-first, bounded per user per sweep so a reader's BYOK endpoint
/// isn't hammered. One user's (or article's) failure never aborts the run.
/// </summary>
public sealed class ArticleSummaryBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ArticleSummaryBackgroundService> log) : PeriodicBackgroundService(log)
{
    /// <summary>Newest-N articles summarised per user per sweep — bounds each reader's API spend.</summary>
    private const int BatchPerUser = 10;
    private static readonly TimeSpan BetweenCalls = TimeSpan.FromSeconds(2);

    protected override string Name => "Article-summary";
    // Stagger after the feed-refresh and full-content backfill workers' startup delays.
    protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(25);
    protected override TimeSpan Period => TimeSpan.FromMinutes(5);

    protected override async Task RunAsync(CancellationToken ct)
    {
        List<Guid> userIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            userIds = await db.Users
                .Where(u => u.AiEnabled && u.AiApiKeyEncrypted != null && u.AiAutoArticle)
                .Select(u => u.Id)
                .ToListAsync(ct);
        }
        if (userIds.Count == 0) return;

        // A fresh scope per user isolates change-tracking and keeps the context small across a run
        // that may make several slow LLM calls.
        foreach (var uid in userIds)
        {
            ct.ThrowIfCancellationRequested();
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ai = scope.ServiceProvider.GetRequiredService<AiSummaryService>();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == uid, ct);
            if (user is null) continue;

            var pending = await db.Articles
                .Where(a => a.Source!.FetchFullContent
                    && a.Source.Subscriptions.Any(s => s.UserId == uid)
                    && !db.ArticleSummaries.Any(x => x.UserId == uid && x.ArticleId == a.Id))
                .OrderByDescending(a => a.PublishedAt) // newest first
                .Take(BatchPerUser)
                .ToListAsync(ct);

            foreach (var article in pending)
            {
                try
                {
                    await ai.SummarizeArticleAsync(user, article, ct, AiJobTrigger.Scheduled);
                }
                catch (AiException ex)
                {
                    // Misconfig or a down endpoint — stop this user's batch rather than hammer it.
                    Log.LogInformation("Stopping article summaries for user {UserId}: {Reason}", uid, ex.Message);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.LogWarning(ex, "Failed to summarise article {ArticleId} for user {UserId}", article.Id, uid);
                }

                await Task.Delay(BetweenCalls, ct);
            }
        }
    }
}
