using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Services;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

/// <summary>Two-factor (TOTP) enrollment lifecycle for the signed-in reader: begin enrollment, confirm a
/// code to turn it on (issuing recovery codes), and disable it. The login challenge itself lives in
/// <see cref="AuthEndpoints"/>.</summary>
public static class TotpEndpoints
{
    public static void MapTotpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/totp").RequireAuthorization();
        group.MapPost("/enroll", Enroll);
        group.MapPost("/confirm", Confirm);
        group.MapPost("/disable", Disable);
    }

    /// <summary>Generates a fresh secret and returns the scannable artifacts. Not active until confirmed.</summary>
    private static async Task<IResult> Enroll(ClaimsPrincipal principal, UserManager<AppUser> users, TotpService totp)
    {
        var user = await users.FindByIdAsync(principal.GetUserId().ToString());
        if (user is null) return Results.Unauthorized();
        if (user.IsTotpEnabled)
            return ApiResults.Fail("Two-factor is already on. Disable it first to re-enroll.");

        var secret = TotpService.GenerateSecret();
        user.TotpSecretEncrypted = totp.Encrypt(secret);
        await users.UpdateAsync(user);

        var uri = TotpService.BuildOtpauthUri(user.Email ?? user.UserName ?? "user", secret);
        return Results.Ok(new TotpEnrollResponse(secret, uri, TotpService.BuildQrSvg(uri)));
    }

    /// <summary>Verifies a code from the just-seeded authenticator, turns 2FA on, and returns one-time
    /// recovery codes (which replace any previous set).</summary>
    private static async Task<IResult> Confirm(
        TotpConfirmRequest req, ClaimsPrincipal principal, UserManager<AppUser> users,
        AppDbContext db, TotpService totp, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(principal.GetUserId().ToString());
        if (user is null) return Results.Unauthorized();

        var secret = totp.TryDecrypt(user.TotpSecretEncrypted);
        if (secret is null) return ApiResults.Fail("Start enrollment first.");
        if (!totp.VerifyCode(secret, req.Code))
            return ApiResults.Fail("That code didn't match. Check your authenticator and try again.");

        user.IsTotpEnabled = true;
        await users.UpdateAsync(user);

        await db.RecoveryCodes.Where(r => r.UserId == user.Id).ExecuteDeleteAsync(ct);
        var codes = TotpService.GenerateRecoveryCodes();
        db.RecoveryCodes.AddRange(codes.Select(c => new RecoveryCode
        {
            UserId = user.Id,
            CodeHash = TotpService.HashRecoveryCode(c),
        }));
        await db.SaveChangesAsync(ct);

        return Results.Ok(new TotpConfirmResponse(codes));
    }

    /// <summary>Turns 2FA off after re-verifying the password, clearing the secret and recovery codes.</summary>
    private static async Task<IResult> Disable(
        TotpDisableRequest req, ClaimsPrincipal principal, UserManager<AppUser> users,
        AppDbContext db, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(principal.GetUserId().ToString());
        if (user is null) return Results.Unauthorized();
        if (!await users.CheckPasswordAsync(user, req.Password))
            return ApiResults.Fail("That password is incorrect.");

        user.IsTotpEnabled = false;
        user.TotpSecretEncrypted = null;
        await users.UpdateAsync(user);
        await db.RecoveryCodes.Where(r => r.UserId == user.Id).ExecuteDeleteAsync(ct);

        return Results.Ok(user.ToDto(await users.GetRolesAsync(user)));
    }
}
