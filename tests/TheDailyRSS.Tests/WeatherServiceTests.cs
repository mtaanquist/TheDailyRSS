using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheDailyRSS.Server.Services;
using Xunit;

namespace TheDailyRSS.Tests;

public class WeatherServiceTests
{
    // Canned Open-Meteo payloads.
    private const string GeoJson = """
        { "results": [ { "name": "Copenhagen", "latitude": 55.6759, "longitude": 12.5655, "country": "Denmark" } ] }
        """;
    private const string ForecastJson = """
        {
          "timezone": "Europe/Copenhagen",
          "current": { "temperature_2m": 12.4, "weather_code": 3 },
          "hourly": {
            "time": ["2026-06-01T00:00", "2026-06-01T01:00", "2026-06-01T02:00"],
            "temperature_2m": [9.0, 14.5, 7.0],
            "weather_code": [0, 61, 3]
          }
        }
        """;

    private static WeatherService Build(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var client = new HttpClient(new StubHandler(responder));
        var api = new ExternalApiClient(new StubFactory(client), Options.Create(new FeedOptions()), NullLogger<ExternalApiClient>.Instance);
        return new WeatherService(api, NullLogger<WeatherService>.Instance);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public void LocationKey_rounds_to_two_decimals_and_is_culture_invariant()
    {
        Assert.Equal("55.68,12.57", WeatherService.LocationKey(55.6759, 12.5655));
        // Nearby coordinates collapse to the same shared key.
        Assert.Equal(WeatherService.LocationKey(55.677, 12.566), WeatherService.LocationKey(55.6759, 12.5655));
    }

    [Fact]
    public async Task Geocode_returns_labelled_coordinate()
    {
        var sut = Build(_ => Json(GeoJson));
        var geo = await sut.GeocodeAsync("Copenhagen", default);
        Assert.NotNull(geo);
        Assert.Equal("Copenhagen, Denmark", geo!.Name);
        Assert.Equal(55.6759, geo.Latitude, 4);
        Assert.Equal(12.5655, geo.Longitude, 4);
    }

    [Fact]
    public async Task Geocode_returns_null_when_nothing_matches()
    {
        var sut = Build(_ => Json("""{ "generationtime_ms": 0.1 }"""));   // no "results"
        Assert.Null(await sut.GeocodeAsync("Nowheresville", default));
    }

    [Fact]
    public async Task Geocode_returns_null_for_blank_query_without_calling_out()
    {
        var called = false;
        var sut = Build(_ => { called = true; return Json(GeoJson); });
        Assert.Null(await sut.GeocodeAsync("   ", default));
        Assert.False(called);
    }

    [Fact]
    public async Task Fetch_maps_current_high_low_and_hourly()
    {
        var date = new DateOnly(2026, 6, 1);
        var sut = Build(_ => Json(ForecastJson));

        var snap = await sut.FetchAsync(55.6759, 12.5655, date, default);

        Assert.NotNull(snap);
        Assert.Equal("55.68,12.57", snap!.LocationKey);
        Assert.Equal(date, snap.EditionDate);
        Assert.Equal("Europe/Copenhagen", snap.TimeZone);
        Assert.Equal(12.4, snap.CurrentTempC, 3);
        Assert.Equal(3, snap.CurrentCode);
        Assert.Equal(14.5, snap.HighTempC, 3);   // max of the hourly series
        Assert.Equal(7.0, snap.LowTempC, 3);     // min of the hourly series
        Assert.Contains("2026-06-01T01:00", snap.HourlyJson);
    }

    [Fact]
    public async Task Fetch_returns_null_when_the_api_fails()
    {
        var sut = Build(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        Assert.Null(await sut.FetchAsync(55.6759, 12.5655, new DateOnly(2026, 6, 1), default));
    }

    [Fact]
    public async Task Fetch_returns_null_when_hourly_is_absent()
    {
        var sut = Build(_ => Json("""{ "timezone": "UTC", "current": { "temperature_2m": 1, "weather_code": 0 } }"""));
        Assert.Null(await sut.FetchAsync(0, 0, new DateOnly(2026, 6, 1), default));
    }

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responder(request));
        }
    }
}
