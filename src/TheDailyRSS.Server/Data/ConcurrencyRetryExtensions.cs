using Microsoft.EntityFrameworkCore;

namespace TheDailyRSS.Server.Data;

public static class ConcurrencyRetryExtensions
{
    /// <summary>Runs a read-modify-save unit of work, retrying on a lost optimistic-concurrency race.
    /// On a <see cref="DbUpdateConcurrencyException"/> (xmin mismatch) or <see cref="DbUpdateException"/>
    /// (a racing insert of the same key) the tracked changes are discarded so <paramref name="work"/>
    /// recomputes against the now-current rows — no field is silently clobbered.</summary>
    public static async Task<T> ExecuteWithRetryAsync<T>(
        this AppDbContext db, Func<Task<T>> work, int maxAttempts = 4)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await work();
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException or DbUpdateException && attempt < maxAttempts)
            {
                db.ChangeTracker.Clear();
            }
        }
    }
}
