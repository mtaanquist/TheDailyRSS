using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TheDailyRSS.Shared;
using Xunit;

namespace TheDailyRSS.Tests.Integration;

/// <summary>Covers the passkey endpoints' server-side behaviour that doesn't need a real authenticator:
/// auth gating, options issuance, the management list, and graceful failure when a ceremony is completed
/// without a live challenge. The cryptographic register/login ceremonies themselves require a browser +
/// authenticator and are verified manually.</summary>
[Collection("integration")]
public sealed class PasskeyTests(AppFixture fx)
{
    private static string U() => $"u{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task A_new_account_has_no_passkeys()
    {
        var (client, _) = await fx.RegisterAsync(U());
        var passkeys = (await client.GetFromJsonAsync<List<PasskeyDto>>("/api/auth/passkeys"))!;
        Assert.Empty(passkeys);
    }

    [Fact]
    public async Task Register_begin_requires_authentication()
    {
        var anon = fx.Factory.CreateClient();
        var resp = await anon.PostAsync("/api/auth/passkeys/register/begin", null);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Register_begin_returns_options_with_a_challenge()
    {
        var (client, _) = await fx.RegisterAsync(U());
        var resp = await client.PostAsync("/api/auth/passkeys/register/begin", null);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("challenge", out var ch));
        Assert.False(string.IsNullOrEmpty(ch.GetString()));
        // Discoverable credential is requested so login can be usernameless.
        Assert.True(doc.RootElement.TryGetProperty("rp", out _));
    }

    [Fact]
    public async Task Login_begin_is_anonymous_and_returns_a_handle_and_options()
    {
        var anon = fx.Factory.CreateClient();
        var resp = await anon.PostAsync("/api/auth/passkeys/login/begin", null);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("handle").GetString()));
        Assert.True(doc.RootElement.GetProperty("options").TryGetProperty("challenge", out _));
    }

    [Fact]
    public async Task Register_complete_without_a_live_challenge_fails_cleanly()
    {
        var (client, _) = await fx.RegisterAsync(U());
        // No register/begin first, so there's no cached challenge.
        var resp = await client.PostAsJsonAsync("/api/auth/passkeys/register/complete",
            new PasskeyRegisterCompleteRequest { ResponseJson = "{}", Nickname = "x" });
        Assert.False(resp.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Login_complete_with_an_unknown_handle_fails_cleanly()
    {
        var anon = fx.Factory.CreateClient();
        var resp = await anon.PostAsJsonAsync("/api/auth/passkeys/login/complete",
            new PasskeyLoginCompleteRequest { Handle = "deadbeef", ResponseJson = "{}" });
        Assert.False(resp.IsSuccessStatusCode);
    }
}
