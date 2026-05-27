using TheDailyRSS.Shared;

namespace TheDailyRSS.Client.Services;

/// <summary>Shared, cached sidebar state (categories + edition archive) used across pages.</summary>
public sealed class AppState(ApiClient api, LocalStorage storage)
{
    private const string ExpandedKey = "tdr.expanded";

    public IReadOnlyList<CategoryDto> Categories { get; private set; } = [];
    public IReadOnlyList<EditionDateDto> EditionDates { get; private set; } = [];
    public bool Loaded { get; private set; }

    public event Action? Changed;

    /// <summary>Raised when something outside the edition view (e.g. an F5/"r" hotkey)
    /// asks the current edition to re-pull its stories.</summary>
    public event Func<Task>? ReloadRequested;

    public Task RequestReloadAsync() => ReloadRequested?.Invoke() ?? Task.CompletedTask;

    /// <summary>Desktop "fill the window" mode: drops the 1100px cap and hides the gutters.</summary>
    public bool Expanded { get; private set; }
    private bool _expandedLoaded;

    public int UnreadTotal => Categories.Sum(c => c.UnreadCount);
    public int SavedCount { get; private set; }

    /// <summary>Compact date label shown in the mobile sticky header; set by the
    /// edition view and cleared when it's disposed. Null on non-edition pages.</summary>
    public string? HeaderDate { get; private set; }

    public void SetHeaderDate(string? label)
    {
        if (HeaderDate == label) return;
        HeaderDate = label;
        Changed?.Invoke();
    }

    public async Task EnsureLoadedAsync()
    {
        if (Loaded) return;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (!_expandedLoaded)
        {
            Expanded = await storage.GetAsync(ExpandedKey) == "1";
            _expandedLoaded = true;
        }
        Categories = await api.GetCategoriesAsync();
        EditionDates = await api.GetEditionDatesAsync();
        Loaded = true;
        Changed?.Invoke();
    }

    public async Task ToggleExpandedAsync()
    {
        Expanded = !Expanded;
        await storage.SetAsync(ExpandedKey, Expanded ? "1" : "0");
        Changed?.Invoke();
    }

    public void SetSavedCount(int count)
    {
        SavedCount = count;
        Changed?.Invoke();
    }

    public void NotifyChanged() => Changed?.Invoke();
}
