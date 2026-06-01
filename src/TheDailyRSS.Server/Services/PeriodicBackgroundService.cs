namespace TheDailyRSS.Server.Services;

/// <summary>
/// Base for the app's recurring background workers. Owns the run loop — an initial delay, then a
/// repeating run/wait — with uniform cancellation handling and "one failed sweep is logged, never
/// fatal" semantics, which were previously copy-pasted into every worker.
///
/// <para>Subclasses supply the cadence (<see cref="InitialDelay"/> + <see cref="Period"/>) and the work
/// (<see cref="RunAsync"/>). <see cref="Period"/> is read fresh each loop, so a worker can schedule the
/// next run dynamically (e.g. "until 23:55 tomorrow"). Workers manage their own <c>DbContext</c> scopes
/// inside <see cref="RunAsync"/> — they differ deliberately (one scope per sweep vs. one per user/source),
/// so the base stays out of scoping.</para>
/// </summary>
public abstract class PeriodicBackgroundService(ILogger log) : BackgroundService
{
    protected ILogger Log { get; } = log;

    /// <summary>A short worker name used in the failure log line.</summary>
    protected abstract string Name { get; }

    /// <summary>How long to wait after host start before the first run.</summary>
    protected abstract TimeSpan InitialDelay { get; }

    /// <summary>How long to wait after one run completes before the next begins. Evaluated fresh every
    /// loop, so a worker can return a dynamic delay (e.g. the time until a fixed clock time).</summary>
    protected abstract TimeSpan Period { get; }

    /// <summary>Performs one sweep. Throwing is caught and logged; the loop continues after the next wait.</summary>
    protected abstract Task RunAsync(CancellationToken ct);

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = InitialDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (delay > TimeSpan.Zero)
            {
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }

            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.LogError(ex, "{Name} run failed", Name);
            }

            delay = Period;
        }
    }
}
