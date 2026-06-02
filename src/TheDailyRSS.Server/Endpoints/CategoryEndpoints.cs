using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Services;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

/// <summary>
/// Categories are a fixed, seeded taxonomy. Users read them (with their own counts);
/// only admins mutate the taxonomy (see <see cref="AdminEndpoints"/>).
/// </summary>
public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/categories").RequireAuthorization();
        group.MapGet("", List);
    }

    private static async Task<IResult> List(ClaimsPrincipal principal, AppDbContext db, IOptions<FeedOptions> opts, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        // Sidebar unread counts are scoped to today's edition, matching the masthead. Counting across
        // all of time made a return-from-hiatus feel overwhelming and never reflected "mark all read".
        var today = EditionClock.Today(opts.Value);

        // Count over the *same* set the edition shows — muted and hidden articles excluded — and collapse
        // duplicate source URLs, so a muted/duplicate story can't keep a category's count above zero after
        // "mark all read". (Previously this counted raw db.Articles, ignoring both, so counts never cleared.)
        var visible = await ArticleQueries.VisibleAsync(db, uid, ct);
        var unreadToday = visible.Where(a =>
            a.EditionDate == today && !a.States.Any(s => s.UserId == uid && s.IsRead));
        var pairs = await unreadToday
            .SelectMany(a => a.Source!.Subscriptions
                .Where(s => s.UserId == uid)
                .Select(s => new { s.CategoryId, a.Url }))
            .Distinct()
            .ToListAsync(ct);
        var unread = pairs.GroupBy(p => p.CategoryId).ToDictionary(g => g.Key, g => g.Count());

        var cats = await db.Categories
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.Slug, c.Color, c.SortOrder, FeedCount = c.Subscriptions.Count(s => s.UserId == uid) })
            .ToListAsync(ct);

        return Results.Ok(cats.Select(c => new CategoryDto(
            c.Id, c.Name, c.Slug, c.Color, c.SortOrder, c.FeedCount,
            unread.TryGetValue(c.Id, out var n) ? n : 0)).ToList());
    }
}
