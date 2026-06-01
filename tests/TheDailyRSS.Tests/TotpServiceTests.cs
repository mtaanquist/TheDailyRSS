using Microsoft.AspNetCore.DataProtection;
using OtpNet;
using TheDailyRSS.Server.Services;
using Xunit;

namespace TheDailyRSS.Tests;

public class TotpServiceTests
{
    private static TotpService Build() => new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Secret_roundtrips_through_encryption()
    {
        var sut = Build();
        var secret = TotpService.GenerateSecret();
        Assert.False(string.IsNullOrWhiteSpace(secret));

        var enc = sut.Encrypt(secret);
        Assert.NotEqual(secret, enc);
        Assert.Equal(secret, sut.TryDecrypt(enc));
    }

    [Fact]
    public void TryDecrypt_returns_null_for_null_or_garbage()
    {
        var sut = Build();
        Assert.Null(sut.TryDecrypt(null));
        Assert.Null(sut.TryDecrypt(""));
        Assert.Null(sut.TryDecrypt("not-a-protected-payload"));
    }

    [Fact]
    public void VerifyCode_accepts_a_current_code_and_rejects_a_wrong_one()
    {
        var sut = Build();
        var secret = TotpService.GenerateSecret();
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();

        Assert.True(sut.VerifyCode(secret, code));
        Assert.False(sut.VerifyCode(secret, "000000"));
        Assert.False(sut.VerifyCode(secret, ""));
    }

    [Fact]
    public void Recovery_codes_are_distinct_and_hash_independent_of_formatting()
    {
        var codes = TotpService.GenerateRecoveryCodes();
        Assert.Equal(10, codes.Count);
        Assert.Equal(codes.Count, codes.Distinct().Count());

        var code = codes[0];
        // Dashes, case and spacing must not affect the stored hash.
        Assert.Equal(TotpService.HashRecoveryCode(code), TotpService.HashRecoveryCode($"  {code.ToUpperInvariant()}  "));
        Assert.NotEqual(TotpService.HashRecoveryCode(code), TotpService.HashRecoveryCode(codes[1]));
    }

    [Fact]
    public void Otpauth_uri_and_qr_carry_the_secret()
    {
        var secret = TotpService.GenerateSecret();
        var uri = TotpService.BuildOtpauthUri("reader@example.com", secret);
        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains($"secret={secret}", uri);
        Assert.Contains("issuer=", uri);

        var svg = TotpService.BuildQrSvg(uri);
        Assert.Contains("<svg", svg);
    }
}
