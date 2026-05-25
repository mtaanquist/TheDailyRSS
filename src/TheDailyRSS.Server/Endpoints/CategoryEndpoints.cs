using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/categories").RequireAuthorization();

        group.MapGet("", List);
        group.MapPost("", Create);
        group.MapPut("/reorder", Reorder);
        group.MapPut("/{id:guid}", Update);
        group.MapDelete("/{id:guid}", Delete);
    }

    private static async Task<IResult> List(ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var cats = await db.Categories
            .Where(c => c.UserId == uid)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(
                c.Id, c.Name, c.Color, c.SortOrder,
                c.Feeds.Count,
                c.Feeds.SelectMany(f => f.Articles).Count(a => !a.IsRead)))
            .ToListAsync();
        return Results.Ok(cats);
    }

    private static async Task<IResult> Create(CreateCategoryRequest req, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var nextOrder = await db.Categories.Where(c => c.UserId == uid).MaxAsync(c => (int?)c.SortOrder) ?? -1;
        var category = new Category
        {
            UserId = uid,
            Name = req.Name.Trim(),
            Color = req.Color,
            SortOrder = nextOrder + 1,
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return Results.Ok(new CategoryDto(category.Id, category.Name, category.Color, category.SortOrder, 0, 0));
    }

    private static async Task<IResult> Update(Guid id, UpdateCategoryRequest req, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == uid);
        if (category is null) return Results.NotFound();
        category.Name = req.Name.Trim();
        category.Color = req.Color;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Delete(Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var deleted = await db.Categories.Where(c => c.Id == id && c.UserId == uid).ExecuteDeleteAsync();
        return deleted > 0 ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> Reorder(ReorderRequest req, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var cats = await db.Categories.Where(c => c.UserId == uid).ToListAsync();
        for (var i = 0; i < req.OrderedIds.Count; i++)
        {
            var c = cats.FirstOrDefault(x => x.Id == req.OrderedIds[i]);
            if (c is not null) c.SortOrder = i;
        }
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
