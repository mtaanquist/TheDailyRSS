using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Services;

/// <summary>Drains the <see cref="AiGenerationQueue"/> serially, running each manual daily/weekly generation
/// off the request thread in its own scope. Serial because a self-hosted model serves one request at a time;
/// running here (not in the HTTP handler) means a browser disconnect no longer aborts the work — the result
/// still lands in the cache and the next poll/visit finds it. Failures are recorded to the admin error log
/// inside <see cref="AiSummaryService"/>; one bad item never stops the worker.</summary>
public sealed class AiGenerationWorker(
    AiGenerationQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<AiGenerationWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var req in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await RunAsync(req, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "AI generation worker item failed for user {UserId} ({Kind})", req.UserId, req.Kind);
            }
            finally
            {
                queue.Complete(req);
            }
        }
    }

    private async Task RunAsync(AiGenerationRequest req, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ai = scope.ServiceProvider.GetRequiredService<AiSummaryService>();

        var user = await db.Users.FindAsync([req.UserId], ct);
        if (user is null) return;

        try
        {
            if (req.Kind == AiSummaryKind.Weekly)
                await ai.GenerateWeeklyEditionAsync(user, req.Start, req.End, ct, AiJobTrigger.Interactive);
            else
                await ai.GenerateAsync(user, AiSummaryKind.Daily, req.Start, req.End, ct, AiJobTrigger.Interactive);
        }
        catch (AiException ex)
        {
            // Non-benign failures are already recorded to the admin error log inside the service; benign ones
            // (e.g. "no articles this week") just leave no result for the client's poll to find. Log either way.
            log.LogInformation("AI generation for user {UserId} ({Kind}) produced no result: {Reason}", req.UserId, req.Kind, ex.Message);
        }
    }
}
