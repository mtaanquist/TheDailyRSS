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
        // "The Weekly" is an archive of Monday–Saturday editions. No date = the current week; a date
        // anchors to whichever week contains it, so the reader can flip back through past Sundays.
        group.MapGet("/weekly", GetWeekly);
        group.MapGet("/weekly/{date}", GetWeekly);
        group.MapPost("/weekly", GenerateWeekly);
        group.MapPost("/weekly/{date}", GenerateWeekly);
        // The caller's own in-flight generation + last error, for the manual-generate poll loop.
        group.MapGet("/activity", GetActivity);
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
        user.AiAutoArticle = req.AutoArticle;

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
        string date, ClaimsPrincipal principal, AppDbContext db, AiGenerationQueue queue, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var d)) return ApiResults.Fail("Invalid date.");
        var user = await db.Users.FindAsync([principal.GetUserId()], ct);
        if (user is null) return Results.Unauthorized();
        if (NotConfigured(user, out var why)) return ApiResults.Fail(why);

        // Generation runs off-request on the background worker; the client polls the cached GET + /activity.
        queue.Enqueue(new AiGenerationRequest(user.Id, AiSummaryKind.Daily, d, d));
        return Results.Accepted();
    }

    private static async Task<IResult> GetWeekly(
        ClaimsPrincipal principal, AiSummaryService ai, IOptions<FeedOptions> opts, CancellationToken ct, string? date = null)
    {
        if (!TryWeek(date, opts.Value, out var start, out var end)) return ApiResults.Fail("Invalid date.");
        var dto = await ai.GetWeeklyEditionAsync(principal.GetUserId(), start, end, ct);
        return dto is null ? Results.NotFound() : Results.Ok(dto);
    }

    private static async Task<IResult> GenerateWeekly(
        ClaimsPrincipal principal, AppDbContext db, AiGenerationQueue queue, IOptions<FeedOptions> opts, CancellationToken ct, string? date = null)
    {
        if (!TryWeek(date, opts.Value, out var start, out var end)) return ApiResults.Fail("Invalid date.");
        var user = await db.Users.FindAsync([principal.GetUserId()], ct);
        if (user is null) return Results.Unauthorized();
        if (NotConfigured(user, out var why)) return ApiResults.Fail(why);

        queue.Enqueue(new AiGenerationRequest(user.Id, AiSummaryKind.Weekly, start, end));
        return Results.Accepted();
    }

    /// <summary>The caller's own in-flight generation (queued or running) plus their most recent failure, so
    /// the manual-generate poll loop can tell "keep waiting" from "done" from "it failed — here's why".</summary>
    private static async Task<IResult> GetActivity(
        ClaimsPrincipal principal, AppDbContext db, AiGenerationQueue queue, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        var running = queue.PendingKinds(uid).Select(k => k.ToString()).ToList();
        var email = await db.Users.Where(u => u.Id == uid).Select(u => u.Email).FirstOrDefaultAsync(ct);
        AiErrorDto? lastError = email is null ? null : await db.AiErrorLogs
            .Where(e => e.User == email)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new AiErrorDto(e.OccurredAt, e.User, e.Kind, e.Trigger, e.Label, e.Message))
            .FirstOrDefaultAsync(ct);
        return Results.Ok(new AiActivityDto(running, lastError));
    }

    /// <summary>True (with a reader-facing reason) when the user's BYOK config isn't complete — so a manual
    /// generate fails fast with clear feedback instead of silently enqueuing work that can't run.</summary>
    private static bool NotConfigured(AppUser user, out string why)
    {
        if (!user.AiEnabled)
        {
            why = "AI summaries are turned off. Enable them in settings.";
            return true;
        }
        if (string.IsNullOrWhiteSpace(user.AiBaseUrl) || string.IsNullOrWhiteSpace(user.AiModel)
            || string.IsNullOrEmpty(user.AiApiKeyEncrypted))
        {
            why = "AI summaries aren't fully configured. Add an endpoint, model and API key in settings.";
            return true;
        }
        why = "";
        return false;
    }

    /// <summary>Resolves the Monday–Saturday week for an anchor date (null = the current week).</summary>
    private static bool TryWeek(string? date, FeedOptions opts, out DateOnly start, out DateOnly end)
    {
        DateOnly anchor;
        if (string.IsNullOrEmpty(date)) anchor = EditionClock.Today(opts);
        else if (!DateOnly.TryParse(date, out anchor)) { start = end = default; return false; }
        (start, end) = AiSummaryService.WeeklyWindow(anchor);
        return true;
    }

    private static AiSettingsDto ToSettingsDto(AppUser u) => new(
        u.AiEnabled, u.AiBaseUrl, u.AiModel, u.AiSystemPrompt, u.AiAutoDaily, u.AiAutoWeekly, u.AiAutoArticle,
        !string.IsNullOrEmpty(u.AiApiKeyEncrypted));

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>True for an absolute http/https URL. The SSRF connect-guard still blocks private
    /// targets at connect time; this just rejects obviously wrong input (file:, gopher:, …) early.</summary>
    private static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
}
