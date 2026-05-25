namespace TheDailyRSS.Server.Data;

/// <summary>
/// The fixed, seeded newspaper taxonomy (Guardian-style). Categories are global and
/// not user-creatable; these GUIDs are stable so <c>HasData</c> stays idempotent.
/// </summary>
public static class CategorySeed
{
    public sealed record Seed(Guid Id, string Name, string Slug, string Color, int SortOrder);

    /// <summary>The category new/unmatched subscriptions fall back to (News).</summary>
    public static readonly Guid DefaultCategoryId = Guid.Parse("11111111-1111-1111-1111-000000000000");

    public static readonly IReadOnlyList<Seed> All = new[]
    {
        new Seed(DefaultCategoryId,                                  "News",        "news",        "#a83020", 0),
        new Seed(Guid.Parse("11111111-1111-1111-1111-000000000001"), "World",       "world",       "#1f5673", 1),
        new Seed(Guid.Parse("11111111-1111-1111-1111-000000000002"), "Politics",    "politics",    "#6a4c93", 2),
        new Seed(Guid.Parse("11111111-1111-1111-1111-000000000003"), "Business",    "business",    "#2e6f40", 3),
        new Seed(Guid.Parse("11111111-1111-1111-1111-000000000004"), "Technology",  "technology",  "#3a6ea5", 4),
        new Seed(Guid.Parse("11111111-1111-1111-1111-000000000005"), "Science",     "science",     "#0b7a75", 5),
        new Seed(Guid.Parse("11111111-1111-1111-1111-000000000006"), "Environment", "environment", "#4f772d", 6),
        new Seed(Guid.Parse("11111111-1111-1111-1111-000000000007"), "Sport",       "sport",       "#c1502e", 7),
        new Seed(Guid.Parse("11111111-1111-1111-1111-000000000008"), "Culture",     "culture",     "#9b2226", 8),
        new Seed(Guid.Parse("11111111-1111-1111-1111-000000000009"), "Lifestyle",   "lifestyle",   "#b5838d", 9),
        new Seed(Guid.Parse("11111111-1111-1111-1111-00000000000a"), "Opinion",     "opinion",     "#7d4f50", 10),
    };
}
