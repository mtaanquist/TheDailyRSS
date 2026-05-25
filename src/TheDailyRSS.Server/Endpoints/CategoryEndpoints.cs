using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
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

    private static async Task<IResult> List(ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var cats = await db.Categories
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(
                c.Id, c.Name, c.Slug, c.Color, c.SortOrder,
                c.Subscriptions.Count(s => s.UserId == uid),
                db.Articles.Count(a =>
                    a.Source!.Subscriptions.Any(s => s.UserId == uid && s.CategoryId == c.Id)
                    && !a.States.Any(st => st.UserId == uid && st.IsRead))))
            .ToListAsync();
        return Results.Ok(cats);
    }
}
