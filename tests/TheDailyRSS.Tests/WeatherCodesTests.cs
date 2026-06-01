using TheDailyRSS.Shared;
using Xunit;

namespace TheDailyRSS.Tests;

public class WeatherCodesTests
{
    [Theory]
    [InlineData(0, "Clear", "sun")]
    [InlineData(2, "Partly cloudy", "cloud-sun")]
    [InlineData(3, "Overcast", "cloud")]
    [InlineData(48, "Fog", "cloud-fog")]
    [InlineData(63, "Rain", "cloud-rain")]
    [InlineData(75, "Snow", "cloud-snow")]
    [InlineData(82, "Rain showers", "cloud-rain")]
    [InlineData(95, "Thunderstorm", "cloud-lightning")]
    public void Describe_maps_known_codes(int code, string label, string icon)
    {
        var (gotLabel, gotIcon) = WeatherCodes.Describe(code);
        Assert.Equal(label, gotLabel);
        Assert.Equal(icon, gotIcon);
    }

    [Fact]
    public void Unknown_code_falls_back_to_a_neutral_cloud()
    {
        var (label, icon) = WeatherCodes.Describe(123);
        Assert.Equal("—", label);
        Assert.Equal("cloud", icon);
    }
}
