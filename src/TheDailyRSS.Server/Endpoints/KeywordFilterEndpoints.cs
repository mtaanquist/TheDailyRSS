using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

/// <summary>Per-user mute words. Matching articles are hidden from editions.</summary>
public static class KeywordFilterEndpoints
{
    public static void MapKeywordEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/keywords").RequireAuthorization();
        group.MapGet("", List);
        group.MapPost("", Add);
        group.MapDelete("/{id:guid}", Delete);
    }

    private static async Task<IResult> List(ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var items = await db.KeywordFilters
            .Where(k => k.UserId == uid)
            .OrderBy(k => k.Term)
            .Select(k => new KeywordFilterDto(k.Id, k.Term, k.Scope))
            .ToListAsync();
        return Results.Ok(items);
    }

    private static async Task<IResult> Add(CreateKeywordRequest req, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var term = req.Term.Trim().ToLowerInvariant();
        if (term.Length == 0) return Results.BadRequest(new { error = "Term can't be empty." });
        if (KeywordMatching.BuildPattern(term) is null)
            return Results.BadRequest(new { error = "Add at least one letter or number to match on." });

        if (await db.KeywordFilters.AnyAsync(k => k.UserId == uid && k.Term == term))
            return Results.Conflict(new { error = "You've already muted that term." });

        var filter = new KeywordFilter { UserId = uid, Term = term, Scope = req.Scope };
        db.KeywordFilters.Add(filter);
        await db.SaveChangesAsync();
        return Results.Ok(new KeywordFilterDto(filter.Id, filter.Term, filter.Scope));
    }

    private static async Task<IResult> Delete(Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var deleted = await db.KeywordFilters.Where(k => k.Id == id && k.UserId == uid).ExecuteDeleteAsync();
        return deleted > 0 ? Results.NoContent() : Results.NotFound();
    }
}
