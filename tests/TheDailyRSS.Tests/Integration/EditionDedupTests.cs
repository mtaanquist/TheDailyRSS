using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;
using Xunit;

namespace TheDailyRSS.Tests.Integration;

/// <summary>The edition de-duplicates articles that share a source URL, and the sidebar counts mirror the
/// edition's filters (muted articles excluded) so "mark all read" can actually clear them.</summary>
[Collection("integration")]
public sealed class EditionDedupTests(AppFixture fx)
{
    private static readonly Guid News = CategorySeed.DefaultCategoryId;
    private static string U() => $"u{Guid.NewGuid():N}@example.com";

    private async Task<(Guid SourceId, FeedSource Source)> SeedSubscribedSourceAsync(Guid userId, string feedUrl, params Article[] articles)
    {
        using var scope = fx.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = new FeedSource { FeedUrl = feedUrl, Title = "Seed " + feedUrl, IconText = "S" };
        db.FeedSources.Add(source);
        foreach (var a in articles) { a.Source = source; source.Articles.Add(a); }
        db.Subscriptions.Add(new Subscription { UserId = userId, SourceId = source.Id, CategoryId = News });
        await db.SaveChangesAsync();
        return (source.Id, source);
    }

    private static Article Art(string feedUrl, int i, string title, string url, DateOnly day, string? summary = "a summary") => new()
    {
        ExternalId = $"{feedUrl}#{i}",
        Title = title,
        Summary = summary,
        Url = url,
        PublishedAt = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddMinutes(-i),
        EditionDate = day,
    };

    [Fact]
    public async Task Edition_collapses_articles_that_share_a_source_url()
    {
        var (client, user) = await fx.RegisterAsync(U());
        var feedUrl = $"https://seed.example/{Guid.NewGuid():N}";
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var shared = $"https://news.example/{Guid.NewGuid():N}";

        // Two distinct feed items pointing at the SAME story URL, plus one genuinely different story.
        await SeedSubscribedSourceAsync(user.Id, feedUrl,
            Art(feedUrl, 1, "Copy A", shared, today),
            Art(feedUrl, 2, "Copy B", shared, today),
            Art(feedUrl, 3, "Different story", $"https://news.example/{Guid.NewGuid():N}", today));

        var ed = (await client.GetFromJsonAsync<EditionDto>($"/api/editions/{today:yyyy-MM-dd}"))!;

        var titles = (ed.Lead is null ? new List<string>() : new List<string> { ed.Lead.Title })
            .Concat(ed.Articles.Select(a => a.Title))
            .Concat(ed.Sections.SelectMany(s => s.Articles.Select(a => a.Title)))
            .Distinct()
            .ToList();

        Assert.Equal(2, titles.Count);                       // the duplicate pair collapses to one
        Assert.Equal(2, ed.UnreadTotal);                     // counted as two distinct stories, not three
        Assert.Contains("Different story", titles);
        Assert.Single(titles, t => t is "Copy A" or "Copy B"); // exactly one copy survives
    }

    [Fact]
    public async Task Category_unread_count_excludes_muted_articles()
    {
        var (client, user) = await fx.RegisterAsync(U());
        var feedUrl = $"https://seed.example/{Guid.NewGuid():N}";
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        await SeedSubscribedSourceAsync(user.Id, feedUrl,
            Art(feedUrl, 1, "Keep me around", $"https://news.example/{Guid.NewGuid():N}", today),
            Art(feedUrl, 2, "Zzqmuted headline", $"https://news.example/{Guid.NewGuid():N}", today));

        // Baseline: both unread in the News category.
        var before = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories"))!;
        Assert.Equal(2, before.First(c => c.Id == News).UnreadCount);

        // Mute one — the count must drop, proving the sidebar honours mutes (it previously did not).
        (await client.PostAsJsonAsync("/api/keywords", new CreateKeywordRequest { Term = "zzqmuted" })).EnsureSuccessStatusCode();

        var after = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories"))!;
        Assert.Equal(1, after.First(c => c.Id == News).UnreadCount);
    }
}
