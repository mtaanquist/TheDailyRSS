using System.Net;
using System.Net.Http.Json;
using OtpNet;
using TheDailyRSS.Shared;
using Xunit;

namespace TheDailyRSS.Tests.Integration;

[Collection("integration")]
public sealed class TwoFactorTests(AppFixture fx)
{
    private static string U() => $"u{Guid.NewGuid():N}@example.com";

    private static string Code(string secret) => new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();

    /// <summary>Enrolls TOTP for a freshly-registered reader and returns the secret + recovery codes.</summary>
    private async Task<(string Secret, List<string> Recovery)> EnableTotpAsync(HttpClient client)
    {
        var enrollResp = await client.PostAsJsonAsync("/api/auth/totp/enroll", new { });
        enrollResp.EnsureSuccessStatusCode();
        var enroll = (await enrollResp.Content.ReadFromJsonAsync<TotpEnrollResponse>())!;

        var confirmResp = await client.PostAsJsonAsync("/api/auth/totp/confirm",
            new TotpConfirmRequest { Code = Code(enroll.Secret) });
        confirmResp.EnsureSuccessStatusCode();
        var confirm = (await confirmResp.Content.ReadFromJsonAsync<TotpConfirmResponse>())!;
        return (enroll.Secret, confirm.RecoveryCodes);
    }

    [Fact]
    public async Task Enrolling_yields_ten_recovery_codes_and_flips_the_flag()
    {
        var (client, _) = await fx.RegisterAsync(U());
        var (_, recovery) = await EnableTotpAsync(client);

        Assert.Equal(10, recovery.Count);
        var me = (await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<UserDto>())!;
        Assert.True(me.TwoFactorEnabled);
    }

    [Fact]
    public async Task Login_demands_a_code_then_accepts_a_valid_one()
    {
        var email = U();
        var (client, _) = await fx.RegisterAsync(email);
        var (secret, _) = await EnableTotpAsync(client);

        var anon = fx.Factory.CreateClient();

        // Password alone is not enough — the server asks for a second factor (without issuing a token).
        var step1 = await anon.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email, Password = "password123" });
        step1.EnsureSuccessStatusCode();
        var pending = (await step1.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.True(pending.RequiresTotp);
        Assert.Null(pending.Auth);

        // With a current code, sign-in completes.
        var step2 = await anon.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email, Password = "password123", TotpCode = Code(secret) });
        step2.EnsureSuccessStatusCode();
        var done = (await step2.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.False(done.RequiresTotp);
        Assert.NotNull(done.Auth);
        Assert.False(string.IsNullOrEmpty(done.Auth!.Token));
    }

    [Fact]
    public async Task A_wrong_code_is_rejected()
    {
        var email = U();
        var (client, _) = await fx.RegisterAsync(email);
        await EnableTotpAsync(client);

        var anon = fx.Factory.CreateClient();
        var resp = await anon.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email, Password = "password123", TotpCode = "000000" });
        Assert.False(resp.IsSuccessStatusCode);
    }

    [Fact]
    public async Task A_recovery_code_works_once()
    {
        var email = U();
        var (client, _) = await fx.RegisterAsync(email);
        var (_, recovery) = await EnableTotpAsync(client);
        var oneTime = recovery[0];

        var anon = fx.Factory.CreateClient();

        var first = await anon.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email, Password = "password123", TotpCode = oneTime });
        first.EnsureSuccessStatusCode();
        var ok = (await first.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.NotNull(ok.Auth);

        // The same code can't be redeemed twice.
        var second = await anon.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email, Password = "password123", TotpCode = oneTime });
        Assert.False(second.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Disabling_clears_the_requirement()
    {
        var email = U();
        var (client, _) = await fx.RegisterAsync(email);
        await EnableTotpAsync(client);

        var disableResp = await client.PostAsJsonAsync("/api/auth/totp/disable",
            new TotpDisableRequest { Password = "password123" });
        disableResp.EnsureSuccessStatusCode();
        var user = (await disableResp.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.False(user.TwoFactorEnabled);

        // Login no longer needs a code.
        var anon = fx.Factory.CreateClient();
        var resp = await anon.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email, Password = "password123" });
        resp.EnsureSuccessStatusCode();
        var login = (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.False(login.RequiresTotp);
        Assert.NotNull(login.Auth);
    }
}
