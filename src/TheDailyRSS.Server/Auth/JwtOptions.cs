namespace TheDailyRSS.Server.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Signing key. Auto-generated &amp; persisted on first run if left blank.</summary>
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "TheDailyRSS";
    public string Audience { get; set; } = "TheDailyRSS";
    public int ExpiryDays { get; set; } = 30;
}
