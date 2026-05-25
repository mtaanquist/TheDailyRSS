using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace TheDailyRSS.Client.Services;

/// <summary>Bridges <see cref="AuthService"/> to Blazor's authorization system.</summary>
public sealed class JwtAuthStateProvider : AuthenticationStateProvider
{
    private readonly AuthService _auth;

    public JwtAuthStateProvider(AuthService auth)
    {
        _auth = auth;
        _auth.AuthChanged += () => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = _auth.CurrentUser;
        if (user is null)
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Email, user.Email),
        }, authenticationType: "jwt");

        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }
}
