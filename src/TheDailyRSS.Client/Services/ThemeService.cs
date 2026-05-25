using Microsoft.JSInterop;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Client.Services;

/// <summary>Applies the active theme to the document root and re-applies on auth/preference changes.</summary>
public sealed class ThemeService(IJSRuntime js, AuthService auth) : IDisposable
{
    public string Effective { get; private set; } = "newsprint";

    public async Task InitializeAsync()
    {
        auth.AuthChanged += OnAuthChanged;
        await ApplyAsync();
    }

    private async void OnAuthChanged()
    {
        try { await ApplyAsync(); } catch { /* document not ready */ }
    }

    public async Task ApplyAsync()
    {
        var pref = auth.CurrentUser?.Preferences.Theme ?? ThemePreference.Newsprint;
        Effective = pref switch
        {
            ThemePreference.Evening => "evening",
            ThemePreference.Auto => IsEvening() ? "evening" : "newsprint",
            _ => "newsprint",
        };
        await js.InvokeVoidAsync("document.documentElement.setAttribute", "data-theme", Effective);

        var font = auth.CurrentUser?.Preferences.HeadlineFont ?? HeadlineFont.PtSerif;
        await js.InvokeVoidAsync("document.documentElement.setAttribute", "data-font", font.ToString().ToLowerInvariant());

        var density = auth.CurrentUser?.Preferences.Density ?? ReadingDensity.Balanced;
        await js.InvokeVoidAsync("document.documentElement.setAttribute", "data-density", density.ToString().ToLowerInvariant());
    }

    /// <summary>Rough "after sunset" heuristic for the Auto theme (local 19:00–06:59).</summary>
    private static bool IsEvening()
    {
        var hour = DateTime.Now.Hour;
        return hour >= 19 || hour < 7;
    }

    public void Dispose() => auth.AuthChanged -= OnAuthChanged;
}
