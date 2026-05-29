using Microsoft.AspNetCore.Components;

namespace TheDailyRSS.Client.Services;

/// <summary>Base component for pages that run API operations with the same busy + error handling.
/// Replaces the repeated <c>_busy = true; _error = null; try { … } catch (ApiException ex)
/// { _error = ex.Message; } finally { _busy = false; }</c> boilerplate.</summary>
public abstract class OperationComponent : ComponentBase
{
    /// <summary>True while a <see cref="RunAsync"/> operation is in flight; bind to disable controls.</summary>
    protected bool Busy { get; private set; }

    /// <summary>The last operation's error message (an <see cref="ApiException.Message"/>), or null.</summary>
    protected string? Error { get; private set; }

    /// <summary>Runs <paramref name="operation"/> with shared busy/error handling: sets <see cref="Busy"/>
    /// for the duration, clears any prior <see cref="Error"/>, surfaces an <see cref="ApiException"/>'s
    /// message into <see cref="Error"/>, and returns true on success (so callers can gate follow-up UI).</summary>
    protected async Task<bool> RunAsync(Func<Task> operation)
    {
        Busy = true;
        Error = null;
        try
        {
            await operation();
            return true;
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
            return false;
        }
        finally
        {
            Busy = false;
        }
    }
}
