using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", Register).AllowAnonymous();
        group.MapPost("/login", Login).AllowAnonymous();

        var secure = group.MapGroup("").RequireAuthorization();
        secure.MapPost("/logout", Logout);
        secure.MapGet("/me", Me);
        secure.MapPut("/profile", UpdateProfile);
        secure.MapPut("/preferences", UpdatePreferences);
        secure.MapPost("/password", ChangePassword);
        secure.MapGet("/sessions", ListSessions);
        secure.MapDelete("/sessions/{id:guid}", RevokeSession);
        secure.MapPost("/sessions/revoke-others", RevokeOthers);
        secure.MapGet("/sync-status", SyncStatus);
    }

    private static async Task<IResult> Register(
        RegisterRequest req, UserManager<AppUser> users, AppDbContext db,
        JwtTokenService tokens, HttpContext http)
    {
        if (await users.FindByEmailAsync(req.Email) is not null)
            return Results.Conflict(new { error = "An account with that email already exists." });

        var user = new AppUser
        {
            UserName = req.Email,
            Email = req.Email,
            DisplayName = req.DisplayName.Trim(),
        };
        var result = await users.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return Results.BadRequest(new { error = string.Join(" ", result.Errors.Select(e => e.Description)) });

        // The very first account to register becomes the instance admin.
        if (await db.Users.CountAsync() == 1)
            await users.AddToRoleAsync(user, Roles.Admin);

        return await IssueAsync(user, users, db, tokens, http);
    }

    private static async Task<IResult> Login(
        LoginRequest req, UserManager<AppUser> users, AppDbContext db,
        JwtTokenService tokens, HttpContext http)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null || !await users.CheckPasswordAsync(user, req.Password))
            return Results.Unauthorized();

        return await IssueAsync(user, users, db, tokens, http);
    }

    private static async Task<IResult> IssueAsync(
        AppUser user, UserManager<AppUser> users, AppDbContext db, JwtTokenService tokens, HttpContext http)
    {
        var ua = http.Request.Headers.UserAgent.ToString();
        var session = new UserSession
        {
            UserId = user.Id,
            UserAgent = ua.Length > 1000 ? ua[..1000] : ua,
            DeviceLabel = DeviceLabel.From(ua),
            IpAddress = http.Connection.RemoteIpAddress?.ToString(),
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        var roles = await users.GetRolesAsync(user);
        var (token, expiresAt) = tokens.CreateToken(user, session.Id, roles);
        return Results.Ok(new AuthResponse(token, expiresAt, user.ToDto(roles)));
    }

    private static async Task<IResult> Logout(System.Security.Claims.ClaimsPrincipal principal, AppDbContext db)
    {
        var sid = principal.GetSessionId();
        await db.Sessions.Where(s => s.Id == sid)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        return Results.NoContent();
    }

    private static async Task<IResult> Me(
        System.Security.Claims.ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        var user = await users.FindByIdAsync(principal.GetUserId().ToString());
        return user is null ? Results.Unauthorized() : Results.Ok(user.ToDto(await users.GetRolesAsync(user)));
    }

    private static async Task<IResult> UpdateProfile(
        UpdateProfileRequest req, System.Security.Claims.ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        var user = await users.FindByIdAsync(principal.GetUserId().ToString());
        if (user is null) return Results.Unauthorized();
        user.DisplayName = req.DisplayName.Trim();
        await users.UpdateAsync(user);
        return Results.Ok(user.ToDto(await users.GetRolesAsync(user)));
    }

    private static async Task<IResult> UpdatePreferences(
        PreferencesDto req, System.Security.Claims.ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        var user = await users.FindByIdAsync(principal.GetUserId().ToString());
        if (user is null) return Results.Unauthorized();
        user.Theme = req.Theme;
        user.HeadlineFont = req.HeadlineFont;
        user.Density = req.Density;
        user.ShowUnread = req.ShowUnread;
        await users.UpdateAsync(user);
        return Results.Ok(user.ToDto(await users.GetRolesAsync(user)));
    }

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest req, System.Security.Claims.ClaimsPrincipal principal,
        UserManager<AppUser> users, AppDbContext db)
    {
        var user = await users.FindByIdAsync(principal.GetUserId().ToString());
        if (user is null) return Results.Unauthorized();
        var result = await users.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        if (!result.Succeeded)
            return Results.BadRequest(new { error = string.Join(" ", result.Errors.Select(e => e.Description)) });

        // A password change should boot every other device (the common "I think I'm compromised" flow).
        // The current session is kept so the user isn't logged out of the device they just used.
        var current = principal.GetSessionId();
        await db.Sessions
            .Where(s => s.UserId == user.Id && s.Id != current && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        return Results.NoContent();
    }

    private static async Task<IResult> ListSessions(
        System.Security.Claims.ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var current = principal.GetSessionId();
        var sessions = await db.Sessions
            .Where(s => s.UserId == uid && s.RevokedAt == null)
            .OrderByDescending(s => s.LastSeenAt)
            .ToListAsync();
        return Results.Ok(sessions.Select(s => s.ToDto(current)));
    }

    private static async Task<IResult> RevokeSession(
        Guid id, System.Security.Claims.ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        await db.Sessions.Where(s => s.Id == id && s.UserId == uid)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeOthers(
        System.Security.Claims.ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var current = principal.GetSessionId();
        await db.Sessions.Where(s => s.UserId == uid && s.Id != current && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        return Results.NoContent();
    }

    private static async Task<IResult> SyncStatus(
        System.Security.Claims.ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var feedCount = await db.Subscriptions.CountAsync(s => s.UserId == uid);
        var catCount = await db.Subscriptions.Where(s => s.UserId == uid).Select(s => s.CategoryId).Distinct().CountAsync();
        var articles = await db.Articles.CountAsync(a => a.Source!.Subscriptions.Any(s => s.UserId == uid));
        var saved = await db.UserArticleStates.CountAsync(s => s.UserId == uid && s.IsSaved);
        var positions = await db.UserArticleStates.CountAsync(s => s.UserId == uid && s.ReadingPositionPercent > 0 && s.ReadingPositionPercent < 100);
        var lastSync = await db.Subscriptions.Where(s => s.UserId == uid).MaxAsync(s => (DateTimeOffset?)s.Source!.LastFetchedAt);
        return Results.Ok(new SyncStatusDto(feedCount, catCount, articles, saved, positions, lastSync));
    }
}

/// <summary>Best-effort friendly device name from a User-Agent string.</summary>
public static class DeviceLabel
{
    public static string From(string ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return "Unknown device";
        var os =
            ua.Contains("iPhone") ? "iPhone" :
            ua.Contains("iPad") ? "iPad" :
            ua.Contains("Android") ? "Android" :
            ua.Contains("Mac OS X") || ua.Contains("Macintosh") ? "Mac" :
            ua.Contains("Windows") ? "Windows" :
            ua.Contains("Linux") ? "Linux" : "Device";
        var browser =
            ua.Contains("Edg/") ? "Edge" :
            ua.Contains("Firefox") ? "Firefox" :
            ua.Contains("Chrome") && !ua.Contains("Edg/") ? "Chrome" :
            ua.Contains("Safari") ? "Safari" : "Browser";
        return $"{os} · {browser}";
    }
}
