using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Services;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

/// <summary>
/// Subscription management. A "feed" in the API is a user's <see cref="Subscription"/> to a
/// globally-shared <see cref="FeedSource"/>; articles are stored once on the source.
/// </summary>
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
        group.MapPost("/refresh", RefreshAll);

        var opml = app.MapGroup("/api/opml").RequireAuthorization();
        opml.MapGet("", ExportOpml);
        opml.MapPost("", ImportOpml);
    }

    private static async Task<IResult> List(ClaimsPrincipal principal, AppDbContext db, Guid? categoryId)
    {
        var uid = principal.GetUserId();
        var query = db.Subscriptions.Where(s => s.UserId == uid);
        if (categoryId is { } cid) query = query.Where(s => s.CategoryId == cid);

        var feeds = await query
            .OrderBy(s => s.SortOrder).ThenBy(s => s.CustomTitle ?? s.Source!.Title)
            .Select(s => new FeedDto(
                s.Id, s.SourceId, s.CategoryId,
                s.CustomTitle ?? s.Source!.Title, s.Source!.FeedUrl, s.Source.SiteUrl, s.Source.IconText, s.SortOrder,
                db.Articles.Count(a => a.SourceId == s.SourceId && !a.States.Any(st => st.UserId == uid && st.IsRead)),
                db.Articles.Count(a => a.SourceId == s.SourceId),
                s.Source.LastFetchedAt, s.Source.LastFetchError))
            .ToListAsync();
        return Results.Ok(feeds);
    }

    private static async Task<IResult> Detect(AddFeedRequest req, FeedDiscoveryService discovery, CancellationToken ct)
    {
        var result = await discovery.DetectAsync(req.Url, ct, discover: !req.Exact);
        return Results.Ok(result);
    }

    private static async Task<IResult> Add(
        AddFeedRequest req, ClaimsPrincipal principal, AppDbContext db,
        FeedDiscoveryService discovery, FeedSourceService sources, FeedFetchService fetcher, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId, ct))
            return ApiResults.Fail("Unknown category.");

        var (feedUrl, parsed) = await discovery.ResolveAsync(req.Url, ct, discover: !req.Exact);
        if (parsed is null)
            return ApiResults.Fail(req.Exact
                ? "That URL isn't a valid RSS or Atom feed."
                : "Couldn't find an RSS or Atom feed at that address.");

        // De-dupe: reuse the shared source if it already exists, else create + fetch it once.
        var (source, created) = await sources.GetOrCreateAsync(feedUrl, parsed.Title, parsed.SiteUrl, ct);

        if (await db.Subscriptions.AnyAsync(s => s.UserId == uid && s.SourceId == source.Id, ct))
            return ApiResults.Conflict("You're already subscribed to that feed.");

        var customTitle = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title!.Trim();
        var nextOrder = await db.Subscriptions.Where(s => s.UserId == uid && s.CategoryId == req.CategoryId)
            .MaxAsync(s => (int?)s.SortOrder, ct) ?? -1;

        var sub = new Subscription
        {
            UserId = uid,
            SourceId = source.Id,
            CategoryId = req.CategoryId,
            CustomTitle = customTitle,
            SortOrder = nextOrder + 1,
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync(ct);

        // Only fetch when the source is brand new; an existing source already has articles.
        if (created) await fetcher.RefreshAsync(source, ct);

        return Results.Ok(new FeedDto(
            sub.Id, source.Id, sub.CategoryId, customTitle ?? source.Title,
            source.FeedUrl, source.SiteUrl, source.IconText, sub.SortOrder,
            await db.Articles.CountAsync(a => a.SourceId == source.Id && !a.States.Any(st => st.UserId == uid && st.IsRead), ct),
            await db.Articles.CountAsync(a => a.SourceId == source.Id, ct),
            source.LastFetchedAt, source.LastFetchError));
    }

    private static async Task<IResult> Update(Guid id, UpdateFeedRequest req, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == uid);
        if (sub is null) return Results.NotFound();
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId))
            return ApiResults.Fail("Unknown category.");
        sub.CustomTitle = req.Title.Trim();
        sub.CategoryId = req.CategoryId;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Delete(Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        // Delete only the subscription; the shared source/articles stay for other subscribers.
        var deleted = await db.Subscriptions.Where(s => s.Id == id && s.UserId == uid).ExecuteDeleteAsync();
        return deleted > 0 ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> Move(Guid id, MoveFeedRequest req, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == uid);
        if (sub is null) return Results.NotFound();
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId))
            return ApiResults.Fail("Unknown category.");
        sub.CategoryId = req.CategoryId;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Refresh(
        Guid id, ClaimsPrincipal principal, AppDbContext db, FeedFetchService fetcher, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        var sub = await db.Subscriptions.Include(s => s.Source)
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == uid, ct);
        if (sub?.Source is null) return Results.NotFound();
        var added = await fetcher.RefreshAsync(sub.Source, ct);
        return Results.Ok(new { added });
    }

    private static async Task<IResult> RefreshAll(
        ClaimsPrincipal principal, AppDbContext db, FeedFetchService fetcher, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        // Distinct shared sources across the user's subscriptions (a source is fetched once).
        var sources = await db.Subscriptions.Where(s => s.UserId == uid)
            .Select(s => s.Source!)
            .Distinct()
            .ToListAsync(ct);
        var added = 0;
        foreach (var source in sources)
            added += await fetcher.RefreshAsync(source, ct);
        return Results.Ok(new { added });
    }

    private static async Task<IResult> ExportOpml(ClaimsPrincipal principal, OpmlService opml, CancellationToken ct)
    {
        var xml = await opml.ExportAsync(principal.GetUserId(), ct);
        return Results.File(System.Text.Encoding.UTF8.GetBytes(xml), "text/x-opml", "subscriptions.opml");
    }

    private static async Task<IResult> ImportOpml(
        HttpRequest request, ClaimsPrincipal principal, OpmlService opml,
        IOptions<FeedOptions> opts, CancellationToken ct)
    {
        var maxBytes = opts.Value.MaxResponseBytes;
        if (request.ContentLength is { } len && len > maxBytes)
            return ApiResults.Fail("OPML file is too large.");

        // Bound the read even when Content-Length is absent (chunked upload).
        using var limited = new StreamReader(request.Body);
        var content = await limited.ReadToEndAsync(ct);
        if (content.Length > maxBytes)
            return ApiResults.Fail("OPML file is too large.");
        if (string.IsNullOrWhiteSpace(content))
            return ApiResults.Fail("Empty OPML.");
        try
        {
            var result = await opml.ImportAsync(principal.GetUserId(), content, ct);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return ApiResults.Fail("Invalid OPML: " + ex.Message);
        }
    }
}
