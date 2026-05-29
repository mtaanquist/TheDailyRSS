using TheDailyRSS.Shared;

namespace TheDailyRSS.Client.Services;

/// <summary>URL/slug helpers for the edition reader, shared by Edition.razor and the Sidebar so the
/// route shapes and slug↔category mapping live in one place (they had drifted — the sidebar's path
/// builder didn't handle the "hidden" pseudo-section, this one does).</summary>
public static class EditionRoutes
{
    /// <summary>Slugs that aren't real categories: the front page and the cross-date pseudo-sections.</summary>
    public static bool IsReserved(string? slug) =>
        string.IsNullOrEmpty(slug) || slug is "today" or "saved" or "hidden";

    /// <summary>The category id for a URL slug, or null for the front page / pseudo-sections / unknown.</summary>
    public static Guid? CategoryIdForSlug(IEnumerable<CategoryDto> categories, string? slug) =>
        IsReserved(slug)
            ? null
            : categories.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase))?.Id;

    /// <summary>Builds a reader URL: "/" or "/today/{date}" for the front page, "/saved", "/hidden",
    /// or "/{slug}" / "/{slug}/{date}" for a section.</summary>
    public static string Path(string slug, DateOnly? date) => slug switch
    {
        "saved" => "/saved",
        "hidden" => "/hidden",
        "today" => date is null ? "/" : $"/today/{date:yyyy-MM-dd}",
        _ => date is null ? $"/{slug}" : $"/{slug}/{date:yyyy-MM-dd}",
    };

    /// <summary>Reads a single query-string value (e.g. <c>?src=…</c>); null if absent.</summary>
    public static string? QueryValue(string query, string key)
    {
        if (string.IsNullOrEmpty(query)) return null;
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0] == key) return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }
}
