using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
        group.MapGet("/summary/daily/{date}", GetDailySummary);
        group.MapPost("/summary/daily/{date}", GenerateDailySummary);
        // "The Weekly" is a single, current curated edition (no archive) anchored to the most recent Saturday.
        group.MapGet("/weekly", GetWeekly);
        group.MapPost("/weekly", GenerateWeekly);
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

        var baseUrl = Clean(req.BaseUrl);
        if (baseUrl is not null && !IsHttpUrl(baseUrl))
            return ApiResults.Fail("The AI base URL must be an http(s) address.");

        var systemPrompt = Clean(req.SystemPrompt);
        if (systemPrompt is { Length: > 4000 })
            return ApiResults.Fail("The interests description is too long (4000 characters max).");

        user.AiEnabled = req.Enabled;
        user.AiBaseUrl = baseUrl;
        user.AiModel = Clean(req.Model);
        user.AiSystemPrompt = systemPrompt;
        user.AiAutoDaily = req.AutoDaily;
        user.AiAutoWeekly = req.AutoWeekly;

        if (req.ClearApiKey)
            user.AiApiKeyEncrypted = null;
        else if (!string.IsNullOrWhiteSpace(req.ApiKey))
            user.AiApiKeyEncrypted = ai.Encrypt(req.ApiKey.Trim());

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToSettingsDto(user));
    }

    private static async Task<IResult> GetDailySummary(
        string date, ClaimsPrincipal principal, AppDbContext db, AiSummaryService ai, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var d)) return ApiResults.Fail("Invalid date.");
        var cached = await ai.GetCachedAsync(principal.GetUserId(), AiSummaryKind.Daily, d, d, ct);
        return cached is null ? Results.NotFound() : Results.Ok(cached);
    }

    private static async Task<IResult> GenerateDailySummary(
        string date, ClaimsPrincipal principal, AppDbContext db, AiSummaryService ai, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var d)) return ApiResults.Fail("Invalid date.");
        var user = await db.Users.FindAsync([principal.GetUserId()], ct);
        if (user is null) return Results.Unauthorized();

        try
        {
            return Results.Ok(await ai.GenerateAsync(user, AiSummaryKind.Daily, d, d, ct));
        }
        catch (AiException ex)
        {
            return ApiResults.Fail(ex.Message);
        }
    }

    private static async Task<IResult> GetWeekly(
        ClaimsPrincipal principal, AiSummaryService ai, IOptions<FeedOptions> opts, CancellationToken ct)
    {
        var (start, end) = AiSummaryService.WeeklyWindow(EditionClock.Today(opts.Value));
        var dto = await ai.GetWeeklyEditionAsync(principal.GetUserId(), start, end, ct);
        return dto is null ? Results.NotFound() : Results.Ok(dto);
    }

    private static async Task<IResult> GenerateWeekly(
        ClaimsPrincipal principal, AppDbContext db, AiSummaryService ai, IOptions<FeedOptions> opts, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([principal.GetUserId()], ct);
        if (user is null) return Results.Unauthorized();

        var (start, end) = AiSummaryService.WeeklyWindow(EditionClock.Today(opts.Value));
        try
        {
            return Results.Ok(await ai.GenerateWeeklyEditionAsync(user, start, end, ct));
        }
        catch (AiException ex)
        {
            return ApiResults.Fail(ex.Message);
        }
    }

    private static AiSettingsDto ToSettingsDto(AppUser u) => new(
        u.AiEnabled, u.AiBaseUrl, u.AiModel, u.AiSystemPrompt, u.AiAutoDaily, u.AiAutoWeekly,
        !string.IsNullOrEmpty(u.AiApiKeyEncrypted));

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>True for an absolute http/https URL. The SSRF connect-guard still blocks private
    /// targets at connect time; this just rejects obviously wrong input (file:, gopher:, …) early.</summary>
    private static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
}
