using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Services;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

public static class AiEndpoints
{
    public static void MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai").RequireAuthorization();
        group.MapGet("/settings", GetSettings);
        group.MapPut("/settings", UpdateSettings);
        group.MapGet("/summary/daily/{date}", (string date, ClaimsPrincipal p, AppDbContext db, AiSummaryService ai, CancellationToken ct) =>
            GetSummary(date, AiSummaryKind.Daily, p, db, ai, ct));
        group.MapPost("/summary/daily/{date}", (string date, ClaimsPrincipal p, AppDbContext db, AiSummaryService ai, CancellationToken ct) =>
            GenerateSummary(date, AiSummaryKind.Daily, p, db, ai, ct));
        group.MapGet("/summary/weekly/{date}", (string date, ClaimsPrincipal p, AppDbContext db, AiSummaryService ai, CancellationToken ct) =>
            GetSummary(date, AiSummaryKind.Weekly, p, db, ai, ct));
        group.MapPost("/summary/weekly/{date}", (string date, ClaimsPrincipal p, AppDbContext db, AiSummaryService ai, CancellationToken ct) =>
            GenerateSummary(date, AiSummaryKind.Weekly, p, db, ai, ct));
    }

    private static async Task<IResult> GetSettings(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([principal.GetUserId()], ct);
        if (user is null) return Results.Unauthorized();
        return Results.Ok(ToSettingsDto(user));
    }

    private static async Task<IResult> UpdateSettings(
        UpdateAiSettingsRequest req, ClaimsPrincipal principal, AppDbContext db, AiSummaryService ai, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([principal.GetUserId()], ct);
        if (user is null) return Results.Unauthorized();

        user.AiEnabled = req.Enabled;
        user.AiBaseUrl = Clean(req.BaseUrl);
        user.AiModel = Clean(req.Model);
        user.AiSystemPrompt = Clean(req.SystemPrompt);
        user.AiAutoDaily = req.AutoDaily;
        user.AiAutoWeekly = req.AutoWeekly;

        if (req.ClearApiKey)
            user.AiApiKeyEncrypted = null;
        else if (!string.IsNullOrWhiteSpace(req.ApiKey))
            user.AiApiKeyEncrypted = ai.Encrypt(req.ApiKey.Trim());

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToSettingsDto(user));
    }

    private static async Task<IResult> GetSummary(
        string date, AiSummaryKind kind, ClaimsPrincipal principal, AppDbContext db, AiSummaryService ai, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var d)) return Results.BadRequest(new { error = "Invalid date." });
        var (start, end) = Period(kind, d);
        var cached = await ai.GetCachedAsync(principal.GetUserId(), kind, start, end, ct);
        return cached is null ? Results.NotFound() : Results.Ok(cached);
    }

    private static async Task<IResult> GenerateSummary(
        string date, AiSummaryKind kind, ClaimsPrincipal principal, AppDbContext db, AiSummaryService ai, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var d)) return Results.BadRequest(new { error = "Invalid date." });
        var user = await db.Users.FindAsync([principal.GetUserId()], ct);
        if (user is null) return Results.Unauthorized();

        var (start, end) = Period(kind, d);
        try
        {
            var dto = await ai.GenerateAsync(user, kind, start, end, ct);
            return Results.Ok(dto);
        }
        catch (AiException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static (DateOnly Start, DateOnly End) Period(AiSummaryKind kind, DateOnly date) =>
        kind == AiSummaryKind.Weekly ? AiSummaryService.WeekRange(date) : (date, date);

    private static AiSettingsDto ToSettingsDto(AppUser u) => new(
        u.AiEnabled, u.AiBaseUrl, u.AiModel, u.AiSystemPrompt, u.AiAutoDaily, u.AiAutoWeekly,
        !string.IsNullOrEmpty(u.AiApiKeyEncrypted));

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
