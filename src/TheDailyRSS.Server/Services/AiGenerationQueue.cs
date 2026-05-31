using System.Collections.Concurrent;
using System.Threading.Channels;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Services;

/// <summary>A queued on-demand AI generation (daily briefing or The Weekly) for a user and period.</summary>
public sealed record AiGenerationRequest(Guid UserId, AiSummaryKind Kind, DateOnly Start, DateOnly End);

/// <summary>In-memory queue of manual AI generations the <see cref="AiGenerationWorker"/> drains serially,
/// off the request thread. Deduped per (user, kind, period): a repeated click while one is pending or
/// running is a no-op, and <see cref="PendingKinds"/> lets the client poll "is mine still being worked on?".
/// In-memory by design — a restart simply empties it, which is correct since no generation survives a restart.</summary>
public sealed class AiGenerationQueue
{
    private readonly Channel<AiGenerationRequest> _channel =
        Channel.CreateBounded<AiGenerationRequest>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropWrite, // a full queue means we're badly backed up; shed extras
            SingleReader = true,
        });

    // Holds every request from enqueue until the worker finishes it (so it covers BOTH pending and running).
    private readonly ConcurrentDictionary<string, AiGenerationRequest> _inflight = new();

    private static string Key(Guid userId, AiSummaryKind kind, DateOnly start, DateOnly end) =>
        $"{userId}:{kind}:{start:O}:{end:O}";

    /// <summary>Enqueues a generation unless an identical one is already pending/running. Returns false when
    /// it was already in flight or the queue is full — either way the caller can report "queued".</summary>
    public bool Enqueue(AiGenerationRequest req)
    {
        var key = Key(req.UserId, req.Kind, req.Start, req.End);
        if (!_inflight.TryAdd(key, req)) return false;
        if (!_channel.Writer.TryWrite(req))
        {
            _inflight.TryRemove(key, out _);
            return false;
        }
        return true;
    }

    /// <summary>The distinct kinds currently pending or running for a user, so a client poll can tell whether
    /// to keep waiting. Covers the queued window too, so there's no gap before the worker picks the item up.</summary>
    public IReadOnlyList<AiSummaryKind> PendingKinds(Guid userId) =>
        _inflight.Values.Where(r => r.UserId == userId).Select(r => r.Kind).Distinct().ToList();

    public IAsyncEnumerable<AiGenerationRequest> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    /// <summary>Releases the dedupe key when the worker finishes an item (success or failure).</summary>
    public void Complete(AiGenerationRequest req) =>
        _inflight.TryRemove(Key(req.UserId, req.Kind, req.Start, req.End), out _);
}
