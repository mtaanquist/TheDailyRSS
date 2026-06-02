using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task Reader_can_list_and_revoke_their_shares()
    {
        var (client, user) = await fx.RegisterAsync(U());
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var (_, ids) = await fx.SeedSourceAsync(user.Id, News, $"https://s.example/{Guid.NewGuid():N}", today, "List me");
        var link = (await (await client.PostAsJsonAsync($"/api/articles/{ids[0]}/share", new { }))
            .Content.ReadFromJsonAsync<ShareLinkDto>())!;

        // The share shows up in the reader's own list.
        var list = (await client.GetFromJsonAsync<List<SharedArticleDto>>("/api/articles/shared"))!;
        Assert.Contains(list, s => s.Token == link.Token && s.ArticleTitle == "List me");

        // Revoking it removes it from the list and makes the public page stop resolving.
        var del = await client.DeleteAsync($"/api/articles/shared/{link.Token}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        list = (await client.GetFromJsonAsync<List<SharedArticleDto>>("/api/articles/shared"))!;
        Assert.DoesNotContain(list, s => s.Token == link.Token);

        var anon = fx.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/share/{link.Token}")).StatusCode);
    }

    [Fact]
    public async Task A_reader_cannot_revoke_someone_elses_share()
    {
        var (owner, ownerUser) = await fx.RegisterAsync(U());
        var (stranger, _) = await fx.RegisterAsync(U());
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var (_, ids) = await fx.SeedSourceAsync(ownerUser.Id, News, $"https://s.example/{Guid.NewGuid():N}", today, "Mine");
        var link = (await (await owner.PostAsJsonAsync($"/api/articles/{ids[0]}/share", new { }))
            .Content.ReadFromJsonAsync<ShareLinkDto>())!;

        // The stranger can't revoke it, and it stays live for the owner.
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/articles/shared/{link.Token}")).StatusCode);
        var anon = fx.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/share/{link.Token}")).StatusCode);
    }

    [Fact]
    public async Task Admin_kill_switch_blocks_new_and_existing_shares()
    {
        var (client, user) = await fx.RegisterAsync(U());
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var (_, ids) = await fx.SeedSourceAsync(user.Id, News, $"https://s.example/{Guid.NewGuid():N}", today, "Kill switch");
        var link = (await (await client.PostAsJsonAsync($"/api/articles/{ids[0]}/share", new { }))
            .Content.ReadFromJsonAsync<ShareLinkDto>())!;
        var anon = fx.Factory.CreateClient();

        try
        {
            await SetSharingDisabledAsync(true);

            // Instance config tells the client sharing is off.
            var cfg = (await client.GetFromJsonAsync<InstanceConfigDto>("/api/instance"))!;
            Assert.False(cfg.SharingEnabled);

            // No new links, and the existing link stops resolving.
            var blocked = await client.PostAsJsonAsync($"/api/articles/{ids[0]}/share", new { });
            Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/share/{link.Token}")).StatusCode);
        }
        finally
        {
            await SetSharingDisabledAsync(false);
        }

        // Re-enabled: the existing link resolves again.
        Assert.True((await client.GetFromJsonAsync<InstanceConfigDto>("/api/instance"))!.SharingEnabled);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/share/{link.Token}")).StatusCode);
    }

    [Fact]
    public async Task The_sharing_admin_setting_is_admin_only()
    {
        var (client, _) = await fx.RegisterAsync(U());   // a later registrant is never admin
        var resp = await client.PutAsJsonAsync("/api/admin/settings/sharing",
            new UpdateSharingSettingsRequest { Enabled = false });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    /// <summary>Flips the instance-wide sharing flag straight in the DB (sidesteps needing an admin token).</summary>
    private async Task SetSharingDisabledAsync(bool disabled)
    {
        using var scope = fx.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == SiteSettingKeys.SharingDisabled);
        if (disabled)
        {
            if (row is null) db.AppSettings.Add(new AppSetting { Key = SiteSettingKeys.SharingDisabled, Value = "true" });
            else row.Value = "true";
        }
        else if (row is not null)
        {
            db.AppSettings.Remove(row);
        }
        await db.SaveChangesAsync();
    }
}
