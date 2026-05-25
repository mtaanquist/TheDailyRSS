using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TheDailyRSS.Server.Auth;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var id)
            ? id
            : throw new UnauthorizedAccessException("Missing or invalid user id claim.");
    }

    public static Guid GetSessionId(this ClaimsPrincipal user)
    {
        var sid = user.FindFirstValue(AppClaims.SessionId);
        return Guid.TryParse(sid, out var id) ? id : Guid.Empty;
    }
}
