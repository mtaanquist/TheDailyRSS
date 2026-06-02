using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;
using Xunit;

namespace TheDailyRSS.Tests.Integration;

/// <summary>Editions skip feed items that carry no readable text — video-only or live-thread placeholders
/// with no summary and no body — while keeping articles that have either.</summary>
[Collection("integration")]
public sealed class SkipEmptyArticleTests(AppFixture fx)
{
    private static readonly Guid News = CategorySeed.DefaultCategoryId;
    private static string U() => $"u{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Edition_and_counts_skip_items_with_no_summary_or_body()
    {
        var (client, user) = await fx.RegisterAsync(U());
        var feedUrl = $"https://seed.example/{Guid.NewGuid():N}";
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        using (var scope = fx.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var source = new FeedSource { FeedUrl = feedUrl, Title = "Mixed feed", IconText = "M" };
            db.FeedSources.Add(source);
            source.Articles.Add(new Article
            {
                ExternalId = feedUrl + "#text", Title = "A real story", Summary = "Has a summary.",
                Url = $"https://news.example/{Guid.NewGuid():N}", PublishedAt = DateTimeOffset.UtcNow, EditionDate = today,
            });
            source.Articles.Add(new Article
            {
                // Video/live placeholder: no summary, no content, no extracted body.
                ExternalId = feedUrl + "#video", Title = "Live: video only", Summary = null, ContentHtml = null, FullContentHtml = null,
                Url = $"https://news.example/{Guid.NewGuid():N}", PublishedAt = DateTimeOffset.UtcNow.AddMinutes(-1), EditionDate = today,
            });
            db.Subscriptions.Add(new Subscription { UserId = user.Id, SourceId = source.Id, CategoryId = News });
            await db.SaveChangesAsync();
        }

        var ed = (await client.GetFromJsonAsync<EditionDto>($"/api/editions/{today:yyyy-MM-dd}"))!;
        var titles = (ed.Lead is null ? new List<string>() : new List<string> { ed.Lead.Title })
            .Concat(ed.Articles.Select(a => a.Title))
            .Concat(ed.Sections.SelectMany(s => s.Articles.Select(a => a.Title)))
            .ToList();

        Assert.Contains("A real story", titles);
        Assert.DoesNotContain("Live: video only", titles);
        Assert.Equal(1, ed.UnreadTotal);

        // And the sidebar count agrees — the empty item never inflates it.
        var cats = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories"))!;
        Assert.Equal(1, cats.First(c => c.Id == News).UnreadCount);
    }
}
