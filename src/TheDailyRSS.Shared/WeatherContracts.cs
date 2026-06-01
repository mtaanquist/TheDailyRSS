namespace TheDailyRSS.Shared;

/// <summary>A day's weather for a reader's configured location. Stored per edition day so paging back
/// to an older edition shows the forecast that was on file for that day.</summary>
public sealed record WeatherDto(
    string Location,
    DateOnly Date,
    double CurrentTempC,
    int CurrentCode,
    double HighTempC,
    double LowTempC,
    List<HourlyWeatherDto> Hourly,
    DateTimeOffset FetchedAt);

/// <summary>One hour of the day's forecast. <paramref name="Time"/> is the location-local ISO timestamp
/// (e.g. <c>2026-06-01T14:00</c>) as returned by the weather API.</summary>
public sealed record HourlyWeatherDto(string Time, double TempC, int Code);

public sealed class SetWeatherLocationRequest
{
    /// <summary>A place to geocode (e.g. "Copenhagen"). Empty clears the saved location.</summary>
    public string Query { get; set; } = "";
}

/// <summary>Maps WMO weather codes (as returned by Open-Meteo) to a short label and a Lucide icon name.
/// Shared so the server and client agree on the vocabulary.</summary>
public static class WeatherCodes
{
    public static (string Label, string Icon) Describe(int code) => code switch
    {
        0 => ("Clear", "sun"),
        1 => ("Mainly clear", "cloud-sun"),
        2 => ("Partly cloudy", "cloud-sun"),
        3 => ("Overcast", "cloud"),
        45 or 48 => ("Fog", "cloud-fog"),
        51 or 53 or 55 => ("Drizzle", "cloud-drizzle"),
        56 or 57 => ("Freezing drizzle", "cloud-drizzle"),
        61 or 63 or 65 => ("Rain", "cloud-rain"),
        66 or 67 => ("Freezing rain", "cloud-rain"),
        71 or 73 or 75 => ("Snow", "cloud-snow"),
        77 => ("Snow grains", "cloud-snow"),
        80 or 81 or 82 => ("Rain showers", "cloud-rain"),
        85 or 86 => ("Snow showers", "cloud-snow"),
        95 or 96 or 99 => ("Thunderstorm", "cloud-lightning"),
        _ => ("—", "cloud"),
    };

    public static string Label(int code) => Describe(code).Label;
    public static string Icon(int code) => Describe(code).Icon;
}
