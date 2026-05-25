using TheDailyRSS.Shared;

namespace TheDailyRSS.Client.Services;

/// <summary>Owns the signed-in user + token, persisting the token to localStorage.</summary>
public sealed class AuthService(ApiClient api, TokenStore store, LocalStorage storage)
{
    private const string TokenKey = "tdr.token";

    public UserDto? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser is not null;

    /// <summary>Raised whenever sign-in state or the user's profile/preferences change.</summary>
    public event Action? AuthChanged;

    /// <summary>Restores a saved session on startup, validating it against the server.</summary>
    public async Task InitializeAsync()
    {
        var token = await storage.GetAsync(TokenKey);
        if (string.IsNullOrEmpty(token)) return;

        store.Token = token;
        try
        {
            CurrentUser = await api.MeAsync();
        }
        catch
        {
            await ClearAsync();
        }
        AuthChanged?.Invoke();
    }

    public async Task LoginAsync(string email, string password)
    {
        var resp = await api.LoginAsync(new LoginRequest { Email = email, Password = password });
        await SetAuthAsync(resp);
    }

    public async Task RegisterAsync(string email, string displayName, string password)
    {
        var resp = await api.RegisterAsync(new RegisterRequest { Email = email, DisplayName = displayName, Password = password });
        await SetAuthAsync(resp);
    }

    public async Task LogoutAsync()
    {
        try { await api.LogoutAsync(); } catch { /* best effort */ }
        await ClearAsync();
        AuthChanged?.Invoke();
    }

    /// <summary>Apply a fresh user snapshot (e.g. after a profile/preferences save).</summary>
    public void UpdateUser(UserDto user)
    {
        CurrentUser = user;
        AuthChanged?.Invoke();
    }

    private async Task SetAuthAsync(AuthResponse resp)
    {
        store.Token = resp.Token;
        await storage.SetAsync(TokenKey, resp.Token);
        CurrentUser = resp.User;
        AuthChanged?.Invoke();
    }

    private async Task ClearAsync()
    {
        store.Token = null;
        CurrentUser = null;
        await storage.RemoveAsync(TokenKey);
    }
}
