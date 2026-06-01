using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Services;

/// <summary>Talks to Open-Meteo (free, key-less) to geocode a place name and fetch a day's forecast,
/// returning a ready-to-store <see cref="WeatherSnapshot"/>. Pure HTTP + mapping — persistence is the
/// caller's job — so it's a stateless singleton over <see cref="ExternalApiClient"/>, which already gives
/// it timeouts, a size cap and graceful "return null on failure" behaviour.</summary>
public sealed class WeatherService(ExternalApiClient api, ILogger<WeatherService> log)
{
    private const string GeocodeBase = "https://geocoding-api.open-meteo.com/v1/search";
    private const string ForecastBase = "https://api.open-meteo.com/v1/forecast";

    /// <summary>The dedup/storage key for a coordinate: rounded to ~2 decimals (~1 km) so nearby readers
    /// share one stored snapshot. Invariant formatting keeps the key stable across cultures.</summary>
    public static string LocationKey(double lat, double lon) =>
        FormattableString.Invariant($"{lat:F2},{lon:F2}");

    /// <summary>Resolves a place name to a labelled coordinate, or null if nothing matched.</summary>
    public async Task<GeocodeResult?> GeocodeAsync(string query, CancellationToken ct)
    {
        var q = query.Trim();
        if (q.Length == 0) return null;

        var url = $"{GeocodeBase}?name={Uri.EscapeDataString(q)}&count=1&language=en&format=json";
        var resp = await api.GetJsonAsync<GeoResponse>(url, ct);
        var hit = resp?.Results?.FirstOrDefault();
        if (hit is null) return null;

        var label = string.Join(", ",
            new[] { hit.Name, hit.Country }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new GeocodeResult(label, hit.Latitude, hit.Longitude);
    }

    /// <summary>Fetches the given edition day's forecast for a coordinate and shapes it into a snapshot,
    /// or null if the call failed or returned nothing usable.</summary>
    public async Task<WeatherSnapshot?> FetchAsync(double lat, double lon, DateOnly editionDate, CancellationToken ct)
    {
        var url = $"{ForecastBase}?latitude={Coord(lat)}&longitude={Coord(lon)}"
            + "&current=temperature_2m,weather_code&hourly=temperature_2m,weather_code"
            + "&timezone=auto&forecast_days=1";

        var f = await api.GetJsonAsync<ForecastResponse>(url, ct);
        var times = f?.Hourly?.Time;
        var temps = f?.Hourly?.Temperature;
        var codes = f?.Hourly?.WeatherCode;
        if (times is null || temps is null || codes is null)
        {
            log.LogDebug("Weather fetch for {Lat},{Lon} returned no hourly data", lat, lon);
            return null;
        }

        var n = Math.Min(times.Count, Math.Min(temps.Count, codes.Count));
        var hourly = new List<HourlyWeatherDto>(n);
        for (var i = 0; i < n; i++)
            hourly.Add(new HourlyWeatherDto(times[i], temps[i], codes[i]));
        if (hourly.Count == 0) return null;

        return new WeatherSnapshot
        {
            LocationKey = LocationKey(lat, lon),
            EditionDate = editionDate,
            Latitude = lat,
            Longitude = lon,
            TimeZone = f!.Timezone ?? "",
            CurrentTempC = f.Current?.Temperature ?? hourly[0].TempC,
            CurrentCode = f.Current?.WeatherCode ?? hourly[0].Code,
            HighTempC = hourly.Max(h => h.TempC),
            LowTempC = hourly.Min(h => h.TempC),
            HourlyJson = JsonSerializer.Serialize(hourly),
            FetchedAt = DateTimeOffset.UtcNow,
        };
    }

    private static string Coord(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    // ── Open-Meteo response shapes (snake_case fields mapped explicitly) ──
    private sealed record GeoResponse(List<GeoResult>? Results);
    private sealed record GeoResult(string Name, double Latitude, double Longitude, string? Country);

    private sealed record ForecastResponse(string? Timezone, CurrentBlock? Current, HourlyBlock? Hourly);

    private sealed record CurrentBlock(
        [property: JsonPropertyName("temperature_2m")] double Temperature,
        [property: JsonPropertyName("weather_code")] int WeatherCode);

    private sealed record HourlyBlock(
        List<string>? Time,
        [property: JsonPropertyName("temperature_2m")] List<double>? Temperature,
        [property: JsonPropertyName("weather_code")] List<int>? WeatherCode);
}

/// <summary>A geocoded place: a display label and its coordinate.</summary>
public sealed record GeocodeResult(string Name, double Latitude, double Longitude);
