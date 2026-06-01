using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using OtpNet;
using QRCoder;

namespace TheDailyRSS.Server.Services;

/// <summary>Two-factor (TOTP) helpers: secret generation, code verification, authenticator-enrollment
/// artifacts (otpauth URI + QR), and single-use recovery codes. The shared secret is encrypted at rest via
/// DataProtection, mirroring how the BYOK AI key is handled. Stateless singleton.</summary>
public sealed class TotpService(IDataProtectionProvider dpProvider)
{
    private const string Issuer = "The Daily RSS";

    private IDataProtector Protector => dpProvider.CreateProtector("TotpSecret");

    /// <summary>A fresh 160-bit base32 secret to seed an authenticator.</summary>
    public static string GenerateSecret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public string Encrypt(string base32Secret) => Protector.Protect(base32Secret);

    /// <summary>Decrypts a stored secret, or null if it can't be unprotected (e.g. key rotation).</summary>
    public string? TryDecrypt(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return null;
        try { return Protector.Unprotect(encrypted); }
        catch { return null; }
    }

    /// <summary>Verifies a 6-digit code against the secret, allowing one step of clock drift either way.</summary>
    public bool VerifyCode(string base32Secret, string code)
    {
        var trimmed = (code ?? "").Trim();
        if (trimmed.Length == 0) return false;
        try
        {
            var totp = new Totp(Base32Encoding.ToBytes(base32Secret));
            return totp.VerifyTotp(trimmed, out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch { return false; }
    }

    public static string BuildOtpauthUri(string email, string base32Secret)
    {
        var label = Uri.EscapeDataString($"{Issuer}:{email}");
        return $"otpauth://totp/{label}?secret={base32Secret}&issuer={Uri.EscapeDataString(Issuer)}&digits=6&period=30";
    }

    /// <summary>An inline SVG QR code for the otpauth URI, safe to render directly (we generate it).</summary>
    public static string BuildQrSvg(string otpauthUri)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(otpauthUri, QRCodeGenerator.ECCLevel.Q);
        return new SvgQRCode(data).GetGraphic(4);
    }

    // ── Recovery codes ──────────────────────────────────────────────────
    /// <summary>Generates <paramref name="count"/> human-friendly recovery codes (e.g. "k7f2a-9qx3m").</summary>
    public static List<string> GenerateRecoveryCodes(int count = 10)
    {
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
            codes.Add($"{RandomBlock(5)}-{RandomBlock(5)}");
        return codes;
    }

    /// <summary>Hex SHA-256 of the normalized code, for at-rest comparison.</summary>
    public static string HashRecoveryCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeRecoveryCode(code)))).ToLowerInvariant();

    /// <summary>Lower-cased with dashes/whitespace stripped, so formatting doesn't affect matching.</summary>
    public static string NormalizeRecoveryCode(string code) =>
        new string((code ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    // Unambiguous alphabet (no 0/o/1/l/i) for codes a human might transcribe.
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    private static string RandomBlock(int len)
    {
        var chars = new char[len];
        for (var i = 0; i < len; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }
}
