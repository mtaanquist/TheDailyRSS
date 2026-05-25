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
