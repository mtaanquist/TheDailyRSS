using System.Text.Json;
using Microsoft.JSInterop;
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

    /// <summary>Attempts sign-in. Returns <c>true</c> when fully signed in, or <c>false</c> when the account
    /// has two-factor on and a code is needed — the caller should then re-call with <paramref name="totpCode"/>.</summary>
    public async Task<bool> LoginAsync(string email, string password, string? totpCode = null)
    {
        var resp = await api.LoginAsync(new LoginRequest { Email = email, Password = password, TotpCode = totpCode });
        if (resp.RequiresTotp) return false;
        await SetAuthAsync(resp.Auth!);
        return true;
    }

    public async Task RegisterAsync(string email, string displayName, string password)
    {
        var resp = await api.RegisterAsync(new RegisterRequest { Email = email, DisplayName = displayName, Password = password });
        await SetAuthAsync(resp);
    }

    /// <summary>Passwordless sign-in with a passkey: fetch a challenge, run the browser WebAuthn ceremony,
    /// then complete the assertion server-side. A passkey is a full credential, so this bypasses TOTP.</summary>
    public async Task LoginWithPasskeyAsync(IJSRuntime js)
    {
        var envelope = await api.PasskeyLoginBeginAsync();
        using var doc = JsonDocument.Parse(envelope);
        var handle = doc.RootElement.GetProperty("handle").GetString()!;
        var optionsJson = doc.RootElement.GetProperty("options").GetRawText();

        var responseJson = await js.InvokeAsync<string>("tdrPasskey.authenticate", optionsJson);
        var resp = await api.PasskeyLoginCompleteAsync(handle, responseJson);
        await SetAuthAsync(resp.Auth!);
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
