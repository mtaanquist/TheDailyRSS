using System.Net;
using System.Net.Http.Json;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;
using Xunit;

namespace TheDailyRSS.Tests.Integration;

/// <summary>The opt-in share feature: an article is private until a reader explicitly shares it, after
/// which an anonymous, server-rendered /share/{token} page serves the masthead + article (with Open
/// Graph tags for link unfurlers) and never leaks who shared it.</summary>
[Collection("integration")]
public sealed class ShareTests(AppFixture fx)
{
    private static readonly Guid News = CategorySeed.DefaultCategoryId;
    private static string U() => $"u{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Articles_are_not_public_until_shared()
    {
        // An anonymous client gets a 404 for any token that has not been generated.
        var anon = fx.Factory.CreateClient();
        var resp = await anon.GetAsync($"/share/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Creating_a_share_requires_auth()
    {
        var anon = fx.Factory.CreateClient();
        var resp = await anon.PostAsync($"/api/articles/{Guid.NewGuid()}/share", null);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Share_creates_an_anonymized_public_page_and_is_idempotent()
    {
        var (client, user) = await fx.RegisterAsync(U(), display: "Jane Reader");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var (_, ids) = await fx.SeedSourceAsync(user.Id, News, $"https://s.example/{Guid.NewGuid():N}", today, "A shareable story");
        var articleId = ids[0];

        // Sharing an owned article returns a link, and re-sharing reuses the same token (idempotent).
        var first = await client.PostAsJsonAsync($"/api/articles/{articleId}/share", new { });
        first.EnsureSuccessStatusCode();
        var link = (await first.Content.ReadFromJsonAsync<ShareLinkDto>())!;
        Assert.NotEqual(Guid.Empty, link.Token);
        Assert.Contains($"/share/{link.Token}", link.Url);

        var second = await client.PostAsJsonAsync($"/api/articles/{articleId}/share", new { });
        var link2 = (await second.Content.ReadFromJsonAsync<ShareLinkDto>())!;
        Assert.Equal(link.Token, link2.Token);

        // The public page is anonymous (no bearer token), is real HTML, and carries the embed payload.
        var anon = fx.Factory.CreateClient();
        var page = await anon.GetAsync($"/share/{link.Token}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);

        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("og:title", html);
        Assert.Contains("A shareable story", html);            // the article title
        Assert.Contains("The Daily RSS", html);                // the masthead
        // Anonymized: no sharer identity and no app chrome.
        Assert.DoesNotContain(user.Email, html);
        Assert.DoesNotContain("Jane Reader", html);
        Assert.DoesNotContain("tdr-sidebar", html);
    }
}
