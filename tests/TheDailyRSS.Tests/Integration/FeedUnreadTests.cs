using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;
using Xunit;

namespace TheDailyRSS.Tests.Integration;

[Collection("integration")]
public sealed class FeedUnreadTests(AppFixture fx)
{
    private static readonly Guid News = CategorySeed.DefaultCategoryId;
    private static string U() => $"u{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Per_source_unread_counts_only_todays_edition()
    {
        var (client, user) = await fx.RegisterAsync(U());
        var feedUrl = $"https://seed.example/{Guid.NewGuid():N}";
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Two unread articles in today's edition, plus the subscription.
        var (sourceId, _) = await fx.SeedSourceAsync(user.Id, News, feedUrl, today, "Today A", "Today B");

        // An older unread article on the same source must NOT inflate the sidebar count.
        using (var scope = fx.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Articles.Add(new Article
            {
                SourceId = sourceId,
                ExternalId = feedUrl + "#old",
                Title = "Two days ago",
                Url = feedUrl + "/old",
                PublishedAt = DateTimeOffset.UtcNow.AddDays(-2),
                EditionDate = today.AddDays(-2),
            });
            await db.SaveChangesAsync();
        }

        var feeds = (await client.GetFromJsonAsync<List<FeedDto>>("/api/feeds"))!;
        var feed = feeds.Single(f => f.SourceId == sourceId);

        Assert.Equal(2, feed.UnreadCount);   // today's two only — the older article is excluded
    }
}
