using System.Collections.Concurrent;

namespace TheDailyRSS.Server.Services;

public enum AiJobKind { Daily, Weekly, Article }

/// <summary>Whether a job was kicked off by the nightly/background worker or by a reader clicking
/// "generate"/"summarise" in the UI.</summary>
public enum AiJobTrigger { Scheduled, Interactive }

/// <summary>An in-flight AI generation. The tracker only holds <see cref="UserId"/>; the admin endpoint
/// resolves it to an email so this service stays free of a DB dependency.</summary>
public sealed record AiJob(
    Guid Id, Guid UserId, AiJobKind Kind, AiJobTrigger Trigger, string? Label, DateTimeOffset StartedAt);

/// <summary>A live, in-memory registry of running AI calls, so the admin can see what's generating right
/// now (scheduled vs. user-initiated). Singleton + thread-safe; entries are transient and never persisted —
/// a restart simply empties it, which is correct since no job survives a restart anyway.</summary>
public sealed class AiJobTracker
{
    private readonly ConcurrentDictionary<Guid, AiJob> _jobs = new();

    /// <summary>Registers a job and returns a token; dispose it (e.g. with <c>using</c>) when the job
    /// finishes — including on exception — to remove it from the registry.</summary>
    public IDisposable Begin(Guid userId, AiJobKind kind, AiJobTrigger trigger, string? label)
    {
        var id = Guid.NewGuid();
        _jobs[id] = new AiJob(id, userId, kind, trigger, label, DateTimeOffset.UtcNow);
        return new Handle(this, id);
    }

    public IReadOnlyList<AiJob> Snapshot() =>
        _jobs.Values.OrderBy(j => j.StartedAt).ToList();

    private void End(Guid id) => _jobs.TryRemove(id, out _);

    private sealed class Handle(AiJobTracker tracker, Guid id) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) tracker.End(id);
        }
    }
}
