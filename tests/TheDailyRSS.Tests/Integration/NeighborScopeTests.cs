using System.Net.Http.Json;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;
using Xunit;

namespace TheDailyRSS.Tests.Integration;

/// <summary>Article prev/next neighbours span the whole day by default, but stay within a category when the
/// reader opened the story from one (passed as ?categoryId=).</summary>
[Collection("integration")]
public sealed class NeighborScopeTests(AppFixture fx)
{
    private static readonly Guid News = CategorySeed.DefaultCategoryId;
    private static readonly Guid Tech = Guid.Parse("11111111-1111-1111-1111-000000000004");
    private static string U() => $"u{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Neighbours_span_the_day_but_can_be_scoped_to_a_category()
    {
        var (client, user) = await fx.RegisterAsync(U());
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var (_, newsIds) = await fx.SeedSourceAsync(user.Id, News, $"https://n.example/{Guid.NewGuid():N}", today, "Solo news");
        await fx.SeedSourceAsync(user.Id, Tech, $"https://t.example/{Guid.NewGuid():N}", today, "Solo tech");
        var newsId = newsIds[0];

        // No category context: the only other story that day (the tech one) is a neighbour.
        var all = (await client.GetFromJsonAsync<ArticleNeighborsDto>($"/api/articles/{newsId}/neighbors"))!;
        var neighbour = all.Prev ?? all.Next;
        Assert.NotNull(neighbour);
        Assert.Equal("Solo tech", neighbour!.Title);

        // Scoped to News: there's no other News story, so no neighbours — the tech one is excluded.
        var scoped = (await client.GetFromJsonAsync<ArticleNeighborsDto>($"/api/articles/{newsId}/neighbors?categoryId={News}"))!;
        Assert.Null(scoped.Prev);
        Assert.Null(scoped.Next);
    }
}
