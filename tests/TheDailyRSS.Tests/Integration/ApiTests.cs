using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;
using Xunit;

namespace TheDailyRSS.Tests.Integration;

[Collection("integration")]
public sealed class ApiTests(AppFixture fx)
{
    private static readonly Guid News = CategorySeed.DefaultCategoryId;
    private static readonly Guid Tech = Guid.Parse("11111111-1111-1111-1111-000000000004");

    private static string U() => $"u{Guid.NewGuid():N}@example.com";

    private static List<string> Titles(EditionDto ed) =>
        ed.Sections.SelectMany(s => s.Articles).Select(a => a.Title)
            .Concat(ed.Lead is null ? [] : new[] { ed.Lead.Title }).ToList();

    [Fact]
    public async Task Only_the_first_registrant_is_admin()
    {
        var (_, second) = await fx.RegisterAsync(U());

        // A later registrant is never admin, and the instance has exactly one admin total.
        Assert.False(second.IsAdmin);
        Assert.Equal(1, await fx.CountAsync(db =>
            db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>()));
    }

    [Fact]
    public async Task Categories_are_seeded_and_admin_gate_enforced()
    {
        var (client, _) = await fx.RegisterAsync(U());
        var cats = await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories");
        Assert.Equal(11, cats!.Count);
        Assert.Contains(cats, c => c.Slug == "technology");

        // A non-admin cannot mutate the taxonomy.
        var resp = await client.PostAsJsonAsync("/api/admin/categories",
            new CreateCategoryRequest { Name = "Nope", Slug = "nope-" + Guid.NewGuid().ToString("N"), Color = "#000" });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Two_users_same_url_share_one_source_with_isolated_read_state()
    {
        var (clientA, a) = await fx.RegisterAsync(U());
        var (clientB, b) = await fx.RegisterAsync(U());
        var url = "https://feed.test/" + Guid.NewGuid().ToString("N");
        var date = new DateOnly(2026, 5, 20);

        var (sourceId, ids) = await fx.SeedSourceAsync(a.Id, News, url, date, "One", "Two", "Three");
        await fx.SeedSourceAsync(b.Id, Tech, url, date); // same url → reuses the source

        // Dedup: one source, three articles, two subscriptions.
        Assert.Equal(1, await fx.CountAsync(db => db.FeedSources.Where(s => s.FeedUrl == url)));
        Assert.Equal(3, await fx.CountAsync(db => db.Articles.Where(x => x.SourceId == sourceId)));

        // A marks one read; B is unaffected; exactly one state row was created (lazy).
        var r = await clientA.PostAsJsonAsync($"/api/articles/{ids[0]}/read", new { value = true });
        r.EnsureSuccessStatusCode();
        Assert.Equal(1, await fx.CountAsync(db => db.UserArticleStates.Where(s => s.UserId == a.Id)));
        Assert.Equal(0, await fx.CountAsync(db => db.UserArticleStates.Where(s => s.UserId == b.Id)));

        var edA = await clientA.GetFromJsonAsync<EditionDto>($"/api/editions/{date:yyyy-MM-dd}");
        var edB = await clientB.GetFromJsonAsync<EditionDto>($"/api/editions/{date:yyyy-MM-dd}");
        Assert.Equal(2, edA!.UnreadTotal);
        Assert.Equal(3, edB!.UnreadTotal);

        // B filed the same source under Technology, so the article carries B's category.
        Assert.All(edB.Sections.SelectMany(s => s.Articles), x => Assert.Equal(Tech, x.CategoryId));
    }

    [Fact]
    public async Task Keyword_filter_hides_from_edition_but_direct_open_still_works()
    {
        var (client, u) = await fx.RegisterAsync(U());
        var url = "https://feed.test/" + Guid.NewGuid().ToString("N");
        var date = new DateOnly(2026, 5, 18);
        var (_, ids) = await fx.SeedSourceAsync(u.Id, News, url, date, "Win a free giveaway today", "Normal headline", "Another story");

        await client.PostAsJsonAsync("/api/keywords", new CreateKeywordRequest { Term = "giveaway", Scope = KeywordScope.Everywhere });

        var ed = await client.GetFromJsonAsync<EditionDto>($"/api/editions/{date:yyyy-MM-dd}");
        var titles = ed!.Sections.SelectMany(s => s.Articles).Select(a => a.Title)
            .Concat(ed.Lead is null ? [] : new[] { ed.Lead.Title }).ToList();
        Assert.DoesNotContain(titles, t => t.Contains("giveaway", StringComparison.OrdinalIgnoreCase));

        // The muted article is still reachable by its id (a held link keeps working).
        var direct = await client.GetAsync($"/api/articles/{ids[0]}");
        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
    }

    [Fact]
    public async Task Keyword_filter_matches_body_text_not_just_title()
    {
        var (client, u) = await fx.RegisterAsync(U());
        var url = "https://feed.test/" + Guid.NewGuid().ToString("N");
        var date = new DateOnly(2026, 5, 19);
        var (_, ids) = await fx.SeedSourceAsync(u.Id, News, url, date, "MacBook Air deal", "Unrelated story");

        // The mute term lives only in the article body (a "Where to Buy" deals block), like The Verge.
        using (var scope = fx.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var a = await db.Articles.FirstAsync(x => x.Id == ids[0]);
            a.ContentHtml = "<p>Lovely laptop.</p><h3>Where to Buy:</h3><a href=\"https://www.amazon.com/dp/x\">Amazon</a>";
            await db.SaveChangesAsync();
        }

        await client.PostAsJsonAsync("/api/keywords", new CreateKeywordRequest { Term = "where to buy", Scope = KeywordScope.Everywhere });

        var ed = await client.GetFromJsonAsync<EditionDto>($"/api/editions/{date:yyyy-MM-dd}");
        var titles = Titles(ed!);
        Assert.DoesNotContain("MacBook Air deal", titles);
        Assert.Contains("Unrelated story", titles);
    }

    [Fact]
    public async Task Keyword_matching_respects_word_boundaries_and_wildcards()
    {
        var (client, u) = await fx.RegisterAsync(U());
        var url = "https://feed.test/" + Guid.NewGuid().ToString("N");
        var date = new DateOnly(2026, 5, 20);
        await fx.SeedSourceAsync(u.Id, News, url, date, "Buyer's guide", "Keep me");

        // A bare term matches whole words, so "buy" must not catch "Buyer".
        await client.PostAsJsonAsync("/api/keywords", new CreateKeywordRequest { Term = "buy", Scope = KeywordScope.TitleOnly });
        var ed = await client.GetFromJsonAsync<EditionDto>($"/api/editions/{date:yyyy-MM-dd}");
        Assert.Contains("Buyer's guide", Titles(ed!));

        // A wildcard term does: "*buy*" catches "Buyer".
        await client.PostAsJsonAsync("/api/keywords", new CreateKeywordRequest { Term = "*buy*", Scope = KeywordScope.TitleOnly });
        ed = await client.GetFromJsonAsync<EditionDto>($"/api/editions/{date:yyyy-MM-dd}");
        var titles = Titles(ed!);
        Assert.DoesNotContain("Buyer's guide", titles);
        Assert.Contains("Keep me", titles);
    }

    [Fact]
    public async Task Front_page_groups_into_capped_sections_ordered_by_taxonomy()
    {
        var (client, u) = await fx.RegisterAsync(U());
        var date = new DateOnly(2026, 5, 15);
        var newsTitles = Enumerable.Range(0, 7).Select(i => $"News {i}").ToArray();
        await fx.SeedSourceAsync(u.Id, News, "https://feed.test/" + Guid.NewGuid().ToString("N"), date, newsTitles);
        await fx.SeedSourceAsync(u.Id, Tech, "https://feed.test/" + Guid.NewGuid().ToString("N"), date, "Tech A", "Tech B", "Tech C");

        var ed = await client.GetFromJsonAsync<EditionDto>($"/api/editions/{date:yyyy-MM-dd}");
        Assert.Equal(2, ed!.Sections.Count);
        // News (SortOrder 0) precedes Technology (SortOrder 4).
        Assert.Equal("News", ed.Sections[0].Name);
        Assert.Equal("Technology", ed.Sections[1].Name);
        // Each section is capped to its front-page slice size (5–8, varied per day/category).
        Assert.All(ed.Sections, s => Assert.True(s.Articles.Count <= 8));
        Assert.InRange(ed.Sections[0].Articles.Count, 5, 7); // 7 news articles → capped into the 5–8 range
        Assert.Equal(3, ed.Sections[1].Articles.Count); // 3 tech articles → all shown (below the cap)
    }

    [Fact]
    public async Task Source_filter_returns_only_that_sources_articles()
    {
        var (client, u) = await fx.RegisterAsync(U());
        var date = new DateOnly(2026, 5, 12);
        var (srcA, _) = await fx.SeedSourceAsync(u.Id, News, "https://feed.test/" + Guid.NewGuid().ToString("N"), date, "Alpha one", "Alpha two");
        await fx.SeedSourceAsync(u.Id, Tech, "https://feed.test/" + Guid.NewGuid().ToString("N"), date, "Beta one", "Beta two", "Beta three");

        var ed = await client.GetFromJsonAsync<EditionDto>($"/api/editions/{date:yyyy-MM-dd}?sourceId={srcA}");

        var all = (ed!.Lead is null ? new List<ArticleSummaryDto>() : [ed.Lead]).Concat(ed.Articles).ToList();
        Assert.Equal(2, all.Count);
        Assert.All(all, a => Assert.StartsWith("Alpha", a.Title));
        Assert.Equal("Seed " + (await SourceUrl(srcA)), ed.CategoryName); // heading = the source's title
    }

    private async Task<string> SourceUrl(Guid sourceId)
    {
        using var scope = fx.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FeedSources.Where(s => s.Id == sourceId).Select(s => s.FeedUrl).FirstAsync();
    }

    [Fact]
    public async Task Mark_edition_read_creates_state_rows_for_untouched_articles()
    {
        var (client, u) = await fx.RegisterAsync(U());
        var url = "https://feed.test/" + Guid.NewGuid().ToString("N");
        var date = new DateOnly(2026, 5, 10);
        await fx.SeedSourceAsync(u.Id, News, url, date, "A", "B", "C");

        var resp = await client.PostAsync($"/api/editions/{date:yyyy-MM-dd}/mark-read", null);
        resp.EnsureSuccessStatusCode();

        var ed = await client.GetFromJsonAsync<EditionDto>($"/api/editions/{date:yyyy-MM-dd}");
        Assert.Equal(0, ed!.UnreadTotal);
        Assert.Equal(3, await fx.CountAsync(db => db.UserArticleStates.Where(s => s.UserId == u.Id && s.IsRead)));
    }
}
