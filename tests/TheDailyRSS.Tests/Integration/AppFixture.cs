using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Services;
using TheDailyRSS.Shared;
using Testcontainers.PostgreSql;
using Xunit;

namespace TheDailyRSS.Tests.Integration;

/// <summary>Boots the real app against a throwaway Postgres container (so ILike etc. run for real).</summary>
public sealed class AppFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        var conn = _db.GetConnectionString();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", conn);
            builder.UseSetting("Jwt:Key", "integration-test-signing-key-0123456789-abcdefghij");
            builder.UseSetting("DataDir", Path.Combine(Path.GetTempPath(), "tdr-tests-" + Guid.NewGuid().ToString("N")));
            builder.ConfigureServices(services =>
            {
                // Drop the background services so tests never reach out to the network.
                foreach (var t in new[] { typeof(FeedRefreshBackgroundService), typeof(AiSummaryBackgroundService), typeof(WeatherBackgroundService), typeof(TickerRefreshBackgroundService) })
                {
                    var hosted = services.SingleOrDefault(d => d.ImplementationType == t);
                    if (hosted is not null) services.Remove(hosted);
                }
            });
        });
        // Touching Services builds the host and runs startup migration + role seed.
        _ = Factory.Services;
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        await _db.DisposeAsync();
    }

    public IServiceScope NewScope() => Factory.Services.CreateScope();

    /// <summary>Registers a user and returns an HttpClient with its bearer token applied.</summary>
    public async Task<(HttpClient Client, UserDto User)> RegisterAsync(string email, string display = "Test User")
    {
        var client = Factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest { Email = email, DisplayName = display, Password = "password123" });
        resp.EnsureSuccessStatusCode();
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.Token);
        return (client, auth.User);
    }

    /// <summary>Seeds a shared source with articles and subscribes the user to it in a category.</summary>
    public async Task<(Guid SourceId, List<Guid> ArticleIds)> SeedSourceAsync(
        Guid userId, Guid categoryId, string feedUrl, DateOnly editionDate, params string[] titles)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var source = await db.FeedSources.FirstOrDefaultAsync(s => s.FeedUrl == feedUrl);
        var articleIds = new List<Guid>();
        if (source is null)
        {
            source = new FeedSource { FeedUrl = feedUrl, Title = "Seed " + feedUrl, IconText = "S" };
            db.FeedSources.Add(source);
            for (var i = 0; i < titles.Length; i++)
            {
                var a = new Article
                {
                    Source = source,
                    ExternalId = $"{feedUrl}#{i}",
                    Title = titles[i],
                    // A summary so the article counts as having content (editions skip body-less items).
                    Summary = $"Summary for {titles[i]}.",
                    Url = $"{feedUrl}/{i}",
                    PublishedAt = new DateTimeOffset(editionDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddMinutes(i),
                    EditionDate = editionDate,
                };
                source.Articles.Add(a);
                articleIds.Add(a.Id);
            }
        }
        else
        {
            articleIds = await db.Articles.Where(a => a.SourceId == source.Id).Select(a => a.Id).ToListAsync();
        }

        db.Subscriptions.Add(new Subscription { UserId = userId, SourceId = source.Id, CategoryId = categoryId });
        await db.SaveChangesAsync();
        return (source.Id, articleIds);
    }

    public async Task<int> CountAsync<T>(Func<AppDbContext, IQueryable<T>> q)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await q(db).CountAsync();
    }
}

[CollectionDefinition("integration")]
public sealed class IntegrationCollection : ICollectionFixture<AppFixture>;
