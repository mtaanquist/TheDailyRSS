using TheDailyRSS.Shared;

namespace TheDailyRSS.Client.Services;

/// <summary>Shared, cached sidebar state (categories + edition archive) used across pages.</summary>
public sealed class AppState(ApiClient api)
{
    public IReadOnlyList<CategoryDto> Categories { get; private set; } = [];
    public IReadOnlyList<EditionDateDto> EditionDates { get; private set; } = [];
    public bool Loaded { get; private set; }

    public event Action? Changed;

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
        Categories = await api.GetCategoriesAsync();
        EditionDates = await api.GetEditionDatesAsync();
        Loaded = true;
        Changed?.Invoke();
    }

    public void SetSavedCount(int count)
    {
        SavedCount = count;
        Changed?.Invoke();
    }

    public void NotifyChanged() => Changed?.Invoke();
}
