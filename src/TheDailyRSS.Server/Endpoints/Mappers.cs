using System.Text.Json;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

public static class Mappers
{
    public static string Initials(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (char.ToUpperInvariant(parts[0][0]).ToString() + char.ToUpperInvariant(parts[^1][0])),
        };
    }

    public static UserDto ToDto(this AppUser u, IEnumerable<string>? roles = null) => new(
        u.Id,
        u.Email ?? "",
        u.DisplayName,
        Initials(u.DisplayName),
        u.CreatedAt,
        roles?.Contains(Auth.Roles.Admin) ?? false,
        u.IsTotpEnabled,
        new PreferencesDto
        {
            Theme = u.Theme, HeadlineFont = u.HeadlineFont, Density = u.Density, ShowUnread = u.ShowUnread,
            HideImages = u.HideImages, AiEnabled = u.AiEnabled,
            ShowWeather = u.ShowWeather, WeatherLocation = u.WeatherLocationName,
        });

    public static SessionDto ToDto(this UserSession s, Guid currentSessionId) => new(
        s.Id, s.DeviceLabel, s.UserAgent, s.IpAddress, s.CreatedAt, s.LastSeenAt, s.Id == currentSessionId);

    public static TickerDto ToDto(this UserTicker ut)
    {
        var t = ut.Ticker;
        var price = t?.Price ?? 0;
        var prev = t?.PreviousClose ?? 0;
        var change = price - prev;
        var pct = prev != 0 ? change / prev * 100 : 0;
        return new TickerDto(ut.Symbol, t?.Name ?? ut.Symbol, t?.Currency ?? "",
            price, prev, change, pct, ut.Promoted, ut.SortOrder, t?.UpdatedAt);
    }

    public static PasskeyDto ToDto(this UserCredential c) => new(c.Id, c.Nickname, c.CreatedAt, c.LastUsedAt);

    public static WeatherDto ToWeatherDto(this WeatherSnapshot s, string location)
    {
        var hourly = string.IsNullOrEmpty(s.HourlyJson)
            ? new List<HourlyWeatherDto>()
            : JsonSerializer.Deserialize<List<HourlyWeatherDto>>(s.HourlyJson) ?? new List<HourlyWeatherDto>();
        return new WeatherDto(location, s.EditionDate, s.CurrentTempC, s.CurrentCode, s.HighTempC, s.LowTempC, hourly, s.FetchedAt);
    }
}
