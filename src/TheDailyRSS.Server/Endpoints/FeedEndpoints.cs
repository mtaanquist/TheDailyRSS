using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Services;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

public static class FeedEndpoints
{
    public static void MapFeedEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/feeds").RequireAuthorization();
        group.MapGet("", List);
        group.MapPost("/detect", Detect);
        group.MapPost("", Add);
        group.MapPut("/{id:guid}", Update);
        group.MapDelete("/{id:guid}", Delete);
        group.MapPost("/{id:guid}/move", Move);
        group.MapPost("/{id:guid}/refresh", Refresh);

        var opml = app.MapGroup("/api/opml").RequireAuthorization();
        opml.MapGet("", ExportOpml);
        opml.MapPost("", ImportOpml);
    }

    private static async Task<IResult> List(ClaimsPrincipal principal, AppDbContext db, Guid? categoryId)
    {
        var uid = principal.GetUserId();
        var query = db.Feeds.Where(f => f.UserId == uid);
        if (categoryId is { } cid) query = query.Where(f => f.CategoryId == cid);

        var feeds = await query
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Title)
            .Select(f => new FeedDto(
                f.Id, f.CategoryId, f.Title, f.FeedUrl, f.SiteUrl, f.IconText, f.SortOrder,
                f.Articles.Count(a => !a.IsRead),
                f.Articles.Count(),
                f.LastFetchedAt, f.LastFetchError))
            .ToListAsync();
        return Results.Ok(feeds);
    }

    private static async Task<IResult> Detect(AddFeedRequest req, FeedDiscoveryService discovery, CancellationToken ct)
    {
        var result = await discovery.DetectAsync(req.Url, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> Add(
        AddFeedRequest req, ClaimsPrincipal principal, AppDbContext db,
        FeedDiscoveryService discovery, FeedFetchService fetcher, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId && c.UserId == uid, ct))
            return Results.BadRequest(new { error = "Unknown category." });

        var (feedUrl, parsed) = await discovery.ResolveAsync(req.Url, ct);
        if (parsed is null)
            return Results.BadRequest(new { error = "Couldn't find an RSS or Atom feed at that address." });

        if (await db.Feeds.AnyAsync(f => f.UserId == uid && f.FeedUrl == feedUrl, ct))
            return Results.Conflict(new { error = "You're already subscribed to that feed." });

        var title = string.IsNullOrWhiteSpace(req.Title) ? parsed.Title : req.Title!.Trim();
        var nextOrder = await db.Feeds.Where(f => f.UserId == uid && f.CategoryId == req.CategoryId)
            .MaxAsync(f => (int?)f.SortOrder, ct) ?? -1;

        var feed = new Feed
        {
            UserId = uid,
            CategoryId = req.CategoryId,
            Title = title,
            FeedUrl = feedUrl,
            SiteUrl = parsed.SiteUrl,
            IconText = IconText.From(title),
            SortOrder = nextOrder + 1,
        };
        db.Feeds.Add(feed);
        await db.SaveChangesAsync(ct);

        // Pull the first batch immediately so the edition isn't empty.
        await fetcher.RefreshAsync(feed, ct);

        return Results.Ok(new FeedDto(
            feed.Id, feed.CategoryId, feed.Title, feed.FeedUrl, feed.SiteUrl, feed.IconText, feed.SortOrder,
            await db.Articles.CountAsync(a => a.FeedId == feed.Id && !a.IsRead, ct),
            await db.Articles.CountAsync(a => a.FeedId == feed.Id, ct),
            feed.LastFetchedAt, feed.LastFetchError));
    }

    private static async Task<IResult> Update(Guid id, UpdateFeedRequest req, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var feed = await db.Feeds.FirstOrDefaultAsync(f => f.Id == id && f.UserId == uid);
        if (feed is null) return Results.NotFound();
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId && c.UserId == uid))
            return Results.BadRequest(new { error = "Unknown category." });
        feed.Title = req.Title.Trim();
        feed.IconText = IconText.From(feed.Title);
        feed.CategoryId = req.CategoryId;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Delete(Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var deleted = await db.Feeds.Where(f => f.Id == id && f.UserId == uid).ExecuteDeleteAsync();
        return deleted > 0 ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> Move(Guid id, MoveFeedRequest req, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var feed = await db.Feeds.FirstOrDefaultAsync(f => f.Id == id && f.UserId == uid);
        if (feed is null) return Results.NotFound();
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId && c.UserId == uid))
            return Results.BadRequest(new { error = "Unknown category." });
        feed.CategoryId = req.CategoryId;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Refresh(
        Guid id, ClaimsPrincipal principal, AppDbContext db, FeedFetchService fetcher, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        var feed = await db.Feeds.FirstOrDefaultAsync(f => f.Id == id && f.UserId == uid, ct);
        if (feed is null) return Results.NotFound();
        var added = await fetcher.RefreshAsync(feed, ct);
        return Results.Ok(new { added });
    }

    private static async Task<IResult> ExportOpml(ClaimsPrincipal principal, OpmlService opml, CancellationToken ct)
    {
        var xml = await opml.ExportAsync(principal.GetUserId(), ct);
        return Results.File(System.Text.Encoding.UTF8.GetBytes(xml), "text/x-opml", "subscriptions.opml");
    }

    private static async Task<IResult> ImportOpml(HttpRequest request, ClaimsPrincipal principal, OpmlService opml, CancellationToken ct)
    {
        using var sr = new StreamReader(request.Body);
        var content = await sr.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(content))
            return Results.BadRequest(new { error = "Empty OPML." });
        try
        {
            var result = await opml.ImportAsync(principal.GetUserId(), content, ct);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = "Invalid OPML: " + ex.Message });
        }
    }
}
