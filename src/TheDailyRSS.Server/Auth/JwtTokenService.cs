using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TheDailyRSS.Server.Data;

namespace TheDailyRSS.Server.Auth;

/// <summary>Mints bearer JWTs that carry the user id and the originating session id.</summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTimeOffset ExpiresAt) CreateToken(AppUser user, Guid sessionId)
    {
        var expires = DateTimeOffset.UtcNow.AddDays(_options.ExpiryDays);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(ClaimTypes.Name, user.DisplayName),
            // The session id doubles as the token id so a revoked session kills the token.
            new(JwtRegisteredClaimNames.Jti, sessionId.ToString()),
            new(AppClaims.SessionId, sessionId.ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

public static class AppClaims
{
    public const string SessionId = "sid";
}
