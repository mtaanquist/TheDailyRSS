using System.Security.Claims;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

/// <summary>Passwordless passkeys (WebAuthn/FIDO2; #38). Registration is authorized (you add a passkey to
/// your signed-in account); login is anonymous and usernameless — a discoverable credential identifies the
/// user, and a successful assertion mints a session directly, skipping TOTP entirely.
///
/// <para>The FIDO2 verifier is built per-request from the request's host/origin, so the relying-party id
/// always matches whatever domain the instance is actually served on (no fixed config to get wrong). The
/// short-lived challenge from each "begin" is held in <see cref="IMemoryCache"/> until its "complete".</para></summary>
public static class PasskeyEndpoints
{
    private static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(5);

    public static void MapPasskeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/passkeys");

        var secure = group.MapGroup("").RequireAuthorization();
        secure.MapGet("", List);
        secure.MapDelete("/{id:guid}", Remove);
        secure.MapPost("/register/begin", RegisterBegin);
        secure.MapPost("/register/complete", RegisterComplete);

        group.MapPost("/login/begin", LoginBegin).AllowAnonymous();
        group.MapPost("/login/complete", LoginComplete).AllowAnonymous();
    }

    private static async Task<IResult> List(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        var creds = await db.UserCredentials
            .Where(c => c.UserId == uid).OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
        return Results.Ok(creds.Select(c => c.ToDto()).ToList());
    }

    private static async Task<IResult> Remove(Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        await db.UserCredentials.Where(c => c.Id == id && c.UserId == uid).ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RegisterBegin(
        ClaimsPrincipal principal, UserManager<AppUser> users, AppDbContext db, IMemoryCache cache, HttpContext http, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(principal.GetUserId().ToString());
        if (user is null) return Results.Unauthorized();

        var fidoUser = new Fido2User
        {
            Id = user.Id.ToByteArray(),
            Name = user.Email ?? user.UserName ?? user.Id.ToString(),
            DisplayName = user.DisplayName,
        };

        var existing = await db.UserCredentials.Where(c => c.UserId == user.Id).Select(c => c.CredentialId).ToListAsync(ct);
        var exclude = existing.Select(id => new PublicKeyCredentialDescriptor(id)).ToList();

        var selection = new AuthenticatorSelection
        {
            RequireResidentKey = true,   // discoverable credential, so login can be usernameless
            UserVerification = UserVerificationRequirement.Preferred,
        };

        var options = BuildFido2(http).RequestNewCredential(fidoUser, exclude, selection, AttestationConveyancePreference.None, null);
        cache.Set(RegKey(user.Id), options.ToJson(), ChallengeTtl);
        return Results.Content(options.ToJson(), "application/json");
    }

    private static async Task<IResult> RegisterComplete(
        PasskeyRegisterCompleteRequest req, ClaimsPrincipal principal, UserManager<AppUser> users,
        AppDbContext db, IMemoryCache cache, HttpContext http, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(principal.GetUserId().ToString());
        if (user is null) return Results.Unauthorized();
        if (cache.Get<string>(RegKey(user.Id)) is not { } optionsJson)
            return ApiResults.Fail("Enrollment timed out. Please try again.");

        AuthenticatorAttestationRawResponse? attestation;
        try { attestation = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(req.ResponseJson); }
        catch { return ApiResults.Fail("Invalid passkey response."); }
        if (attestation is null) return ApiResults.Fail("Invalid passkey response.");

        var options = CredentialCreateOptions.FromJson(optionsJson);
        Task<bool> IsUnique(IsCredentialIdUniqueToUserParams p, CancellationToken c) =>
            db.UserCredentials.AllAsync(x => x.CredentialId != p.CredentialId, c);

        Fido2.CredentialMakeResult made;
        try { made = await BuildFido2(http).MakeNewCredentialAsync(attestation, options, IsUnique, null, ct); }
        catch (Fido2VerificationException ex) { return ApiResults.Fail($"Couldn't register that passkey: {ex.Message}"); }

        if (made.Result is not { } success) return ApiResults.Fail("Couldn't register that passkey.");

        cache.Remove(RegKey(user.Id));
        db.UserCredentials.Add(new UserCredential
        {
            UserId = user.Id,
            CredentialId = attestation.Id,
            PublicKey = success.PublicKey,
            SignCount = 0,
            AaGuid = success.Aaguid,
            Nickname = string.IsNullOrWhiteSpace(req.Nickname) ? "Passkey" : req.Nickname!.Trim(),
        });
        await db.SaveChangesAsync(ct);

        var saved = await db.UserCredentials.FirstAsync(c => c.UserId == user.Id && c.CredentialId == attestation.Id, ct);
        return Results.Ok(saved.ToDto());
    }

    private static IResult LoginBegin(IMemoryCache cache, HttpContext http)
    {
        // Usernameless: empty allow-list lets the authenticator offer any discoverable credential for this RP.
        var options = BuildFido2(http).GetAssertionOptions(new List<PublicKeyCredentialDescriptor>(), UserVerificationRequirement.Preferred, null);
        var handle = Guid.NewGuid().ToString("N");
        cache.Set(LoginKey(handle), options.ToJson(), ChallengeTtl);

        // Envelope the FIDO2 options (which own their base64url JSON) alongside the lookup handle.
        var envelope = $"{{\"handle\":\"{handle}\",\"options\":{options.ToJson()}}}";
        return Results.Content(envelope, "application/json");
    }

    private static async Task<IResult> LoginComplete(
        PasskeyLoginCompleteRequest req, AppDbContext db, IMemoryCache cache, UserManager<AppUser> users,
        JwtTokenService tokens, HttpContext http, CancellationToken ct)
    {
        if (cache.Get<string>(LoginKey(req.Handle)) is not { } optionsJson)
            return ApiResults.Fail("Sign-in timed out. Please try again.");

        AuthenticatorAssertionRawResponse? assertion;
        try { assertion = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(req.ResponseJson); }
        catch { return ApiResults.Fail("Invalid passkey response."); }
        if (assertion is null) return ApiResults.Fail("Invalid passkey response.");

        var cred = await db.UserCredentials.FirstOrDefaultAsync(c => c.CredentialId == assertion.Id, ct);
        if (cred is null) return ApiResults.Fail("This passkey isn't registered.");

        var options = AssertionOptions.FromJson(optionsJson);
        Task<bool> IsOwner(IsUserHandleOwnerOfCredentialIdParams p, CancellationToken c) =>
            Task.FromResult(p.UserHandle.AsSpan().SequenceEqual(cred.UserId.ToByteArray()));

        AssertionVerificationResult result;
        try
        {
            result = await BuildFido2(http).MakeAssertionAsync(
                assertion, options, cred.PublicKey, (uint)cred.SignCount, IsOwner, null, ct);
        }
        catch (Fido2VerificationException ex) { return ApiResults.Fail($"Sign-in failed: {ex.Message}"); }

        cred.SignCount = result.Counter;
        cred.LastUsedAt = DateTimeOffset.UtcNow;
        cache.Remove(LoginKey(req.Handle));

        var user = await users.FindByIdAsync(cred.UserId.ToString());
        if (user is null) return Results.Unauthorized();
        await db.SaveChangesAsync(ct);

        var auth = await AuthEndpoints.IssueAsync(user, users, db, tokens, http);
        return Results.Ok(new LoginResponse(false, auth));
    }

    private static IFido2 BuildFido2(HttpContext http)
    {
        var req = http.Request;
        return new Fido2(new Fido2Configuration
        {
            ServerDomain = req.Host.Host,                       // effective RP id, e.g. "localhost" / "rss.example.com"
            ServerName = "The Daily RSS",
            Origins = new HashSet<string> { $"{req.Scheme}://{req.Host.Value}" },
        }, metadataService: null);
    }

    private static string RegKey(Guid userId) => $"passkey:reg:{userId}";
    private static string LoginKey(string handle) => $"passkey:login:{handle}";
}
