using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

/// <summary>Per-user mute rules that target a single captured feed-item field/value pair
/// (e.g. <c>category=guides</c>). Created from the article-detail "Filter…" modal; managed
/// alongside keyword filters on the Filters settings page.</summary>
public static class FieldFilterEndpoints
{
    public static void MapFieldFilterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/field-filters").RequireAuthorization();
        group.MapGet("", List);
        group.MapPost("", Add);
        group.MapDelete("/{id:guid}", Delete);
    }

    private static async Task<IResult> List(ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var items = await db.FieldFilters
            .Where(f => f.UserId == uid)
            .OrderBy(f => f.FieldKey).ThenBy(f => f.Value)
            .Select(f => new FieldFilterDto(
                f.Id, f.FieldKey, f.Operator, f.Value, f.SourceId,
                f.Source != null ? f.Source.Title : null))
            .ToListAsync();
        return Results.Ok(items);
    }

    private static async Task<IResult> Add(CreateFieldFilterRequest req, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var key = (req.FieldKey ?? "").Trim().ToLowerInvariant();
        var value = (req.Value ?? "").Trim().ToLowerInvariant();
        if (key.Length == 0 || value.Length == 0)
            return Results.BadRequest(new { error = "Field and value are both required." });
        if (req.Operator != FieldFilterOperator.Equals)
            return Results.BadRequest(new { error = "That operator isn't supported yet." });

        // If the rule is feed-scoped, make sure the user actually subscribes to the feed —
        // otherwise the rule would silently apply to nothing.
        if (req.SourceId is { } sid &&
            !await db.Subscriptions.AnyAsync(s => s.UserId == uid && s.SourceId == sid))
            return Results.BadRequest(new { error = "You don't subscribe to that feed." });

        var existing = await db.FieldFilters.AnyAsync(f =>
            f.UserId == uid && f.FieldKey == key && f.Operator == req.Operator
            && f.Value == value && f.SourceId == req.SourceId);
        if (existing) return Results.Conflict(new { error = "You've already muted that field." });

        var filter = new FieldFilter
        {
            UserId = uid,
            FieldKey = key,
            Operator = req.Operator,
            Value = value,
            SourceId = req.SourceId,
        };
        db.FieldFilters.Add(filter);
        await db.SaveChangesAsync();

        string? sourceTitle = null;
        if (filter.SourceId is { } s)
            sourceTitle = await db.FeedSources.Where(x => x.Id == s).Select(x => x.Title).FirstOrDefaultAsync();
        return Results.Ok(new FieldFilterDto(filter.Id, filter.FieldKey, filter.Operator, filter.Value, filter.SourceId, sourceTitle));
    }

    private static async Task<IResult> Delete(Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var deleted = await db.FieldFilters.Where(f => f.Id == id && f.UserId == uid).ExecuteDeleteAsync();
        return deleted > 0 ? Results.NoContent() : Results.NotFound();
    }
}
