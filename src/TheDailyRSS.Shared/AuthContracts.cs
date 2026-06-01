using System.ComponentModel.DataAnnotations;

namespace TheDailyRSS.Shared;

public sealed class RegisterRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, MinLength(2)]
    public string DisplayName { get; set; } = "";

    [Required, MinLength(8)]
    public string Password { get; set; } = "";
}

public sealed class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";

    /// <summary>A 6-digit authenticator code or a recovery code, supplied on the second step when the
    /// account has two-factor enabled. Null/empty on the first step.</summary>
    public string? TotpCode { get; set; }
}

public sealed class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = "";

    [Required, MinLength(8)]
    public string NewPassword { get; set; } = "";
}

public sealed class ChangeEmailRequest
{
    [Required]
    public string CurrentPassword { get; set; } = "";

    [Required, EmailAddress]
    public string NewEmail { get; set; } = "";
}

/// <summary>Returned on register/login. The token is a bearer JWT.</summary>
public sealed record AuthResponse(string Token, DateTimeOffset ExpiresAt, UserDto User);

/// <summary>The result of a login attempt: either a completed sign-in (<see cref="Auth"/>) or a prompt to
/// supply a second factor (<see cref="RequiresTotp"/>), when the password was correct but the account has
/// two-factor enabled and no valid code was provided.</summary>
public sealed record LoginResponse(bool RequiresTotp, AuthResponse? Auth);

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Initials,
    DateTimeOffset CreatedAt,
    bool IsAdmin,
    bool TwoFactorEnabled,
    PreferencesDto Preferences);

// ── Two-factor (TOTP) enrollment (#38) ──────────────────────────────────
/// <summary>The secret + scannable artifacts returned when a reader begins TOTP enrollment. Enrollment
/// isn't active until a generated code is confirmed.</summary>
public sealed record TotpEnrollResponse(string Secret, string OtpauthUri, string QrSvg);

public sealed class TotpConfirmRequest
{
    [Required]
    public string Code { get; set; } = "";
}

/// <summary>Returned once when TOTP is first enabled: the plaintext recovery codes to store safely.</summary>
public sealed record TotpConfirmResponse(List<string> RecoveryCodes);

public sealed class TotpDisableRequest
{
    [Required]
    public string Password { get; set; } = "";
}

public sealed record PreferencesDto
{
    public ThemePreference Theme { get; set; } = ThemePreference.Newsprint;
    public HeadlineFont HeadlineFont { get; set; } = HeadlineFont.PtSerif;
    public ReadingDensity Density { get; set; } = ReadingDensity.Balanced;
    public bool ShowUnread { get; set; } = true;

    /// <summary>"No pictures" mode — hide all images for this reader (issue #41).</summary>
    public bool HideImages { get; set; }

    /// <summary>Read-only mirror of the AI opt-in, so the client can gate AI affordances without an
    /// extra request. Managed via the AI settings endpoint, not the preferences endpoint.</summary>
    public bool AiEnabled { get; set; }

    /// <summary>Show the at-a-glance weather in the masthead (issue #33). Saved via the preferences endpoint.</summary>
    public bool ShowWeather { get; set; }

    /// <summary>Read-only mirror of the reader's geocoded weather location label, or null if unset.
    /// Managed via the weather location endpoint, not the preferences endpoint.</summary>
    public string? WeatherLocation { get; set; }
}

public sealed class UpdateProfileRequest
{
    [Required, MinLength(2)]
    public string DisplayName { get; set; } = "";
}

/// <summary>A signed-in device, surfaced on the "Sync &amp; devices" screen.</summary>
public sealed record SessionDto(
    Guid Id,
    string DeviceLabel,
    string UserAgent,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    bool IsCurrent);
