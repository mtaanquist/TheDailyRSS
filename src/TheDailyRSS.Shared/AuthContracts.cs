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

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Initials,
    DateTimeOffset CreatedAt,
    bool IsAdmin,
    PreferencesDto Preferences);

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
