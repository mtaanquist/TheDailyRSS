using Microsoft.EntityFrameworkCore;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Services;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

/// <summary>Admin-only management of the global category taxonomy and site-wide settings.</summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var cats = app.MapGroup("/api/admin/categories").RequireAuthorization(Roles.Admin);
        cats.MapGet("", List);
        cats.MapPost("", Create);
        cats.MapPut("/reorder", Reorder);
        cats.MapPut("/{id:guid}", Update);
        cats.MapDelete("/{id:guid}", Delete);

        var settings = app.MapGroup("/api/admin/settings").RequireAuthorization(Roles.Admin);
        settings.MapGet("/ai-house-style", GetHouseStyle);
        settings.MapPut("/ai-house-style", SetHouseStyle);
    }

    private static async Task<IResult> GetHouseStyle(AppDbContext db, CancellationToken ct)
    {
        var stored = await db.AppSettings
            .Where(s => s.Key == SiteSettingKeys.AiHouseStyle)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        var isDefault = string.IsNullOrWhiteSpace(stored);
        var value = isDefault ? AiSummaryService.DefaultHouseStyle : stored!;
        return Results.Ok(HouseStyleDto(value, isDefault));
    }

    private static async Task<IResult> SetHouseStyle(UpdateAiHouseStyleRequest req, AppDbContext db, CancellationToken ct)
    {
        var value = req.Value?.Trim();
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == SiteSettingKeys.AiHouseStyle, ct);

        // A blank value clears the override (reverts to the built-in default).
        if (string.IsNullOrWhiteSpace(value))
        {
            if (row is not null) db.AppSettings.Remove(row);
            await db.SaveChangesAsync(ct);
            return Results.Ok(HouseStyleDto(AiSummaryService.DefaultHouseStyle, true));
        }

        if (value.Length > 8000) return ApiResults.Fail("The house style is too long (8000 characters max).");

        if (row is null)
            db.AppSettings.Add(new AppSetting { Key = SiteSettingKeys.AiHouseStyle, Value = value });
        else
            row.Value = value;
        await db.SaveChangesAsync(ct);
        return Results.Ok(HouseStyleDto(value, false));
    }

    private static AiHouseStyleDto HouseStyleDto(string value, bool isDefault) => new(
        value, isDefault, AiSummaryService.DefaultHouseStyle,
        AiSummaryService.DailyBriefingRules, AiSummaryService.WeeklyCurationRules);

    private static async Task<IResult> List(AppDbContext db)
    {
        var cats = await db.Categories
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.Color, c.SortOrder,
                c.Subscriptions.Count, 0))
            .ToListAsync();
        return Results.Ok(cats);
    }

    private static async Task<IResult> Create(CreateCategoryRequest req, AppDbContext db)
    {
        var slug = req.Slug.Trim().ToLowerInvariant();
        if (slug.Length == 0) return ApiResults.Fail("Slug is required.");
        if (await db.Categories.AnyAsync(c => c.Slug == slug))
            return ApiResults.Conflict("A category with that slug already exists.");

        var nextOrder = await db.Categories.MaxAsync(c => (int?)c.SortOrder) ?? -1;
        var category = new Category
        {
            Name = req.Name.Trim(),
            Slug = slug,
            Color = req.Color,
            SortOrder = nextOrder + 1,
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return Results.Ok(new CategoryDto(category.Id, category.Name, category.Slug, category.Color, category.SortOrder, 0, 0));
    }

    private static async Task<IResult> Update(Guid id, UpdateCategoryRequest req, AppDbContext db)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null) return Results.NotFound();
        category.Name = req.Name.Trim();
        category.Color = req.Color;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Delete(Guid id, AppDbContext db)
    {
        // The FK is Restrict: refuse to delete a category that still has subscriptions filed under it.
        if (await db.Subscriptions.AnyAsync(s => s.CategoryId == id))
            return ApiResults.Conflict("Category is in use by subscriptions. Reassign them first.");
        var deleted = await db.Categories.Where(c => c.Id == id).ExecuteDeleteAsync();
        return deleted > 0 ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> Reorder(ReorderRequest req, AppDbContext db)
    {
        var cats = await db.Categories.ToListAsync();
        for (var i = 0; i < req.OrderedIds.Count; i++)
        {
            var c = cats.FirstOrDefault(x => x.Id == req.OrderedIds[i]);
            if (c is not null) c.SortOrder = i;
        }
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
