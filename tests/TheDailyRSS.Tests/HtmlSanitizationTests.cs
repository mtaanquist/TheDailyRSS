using TheDailyRSS.Server.Services;
using Xunit;

namespace TheDailyRSS.Tests;

public class HtmlSanitizationTests
{
    private readonly HtmlSanitizationService _sanitizer = new();

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(document.cookie)>")]
    [InlineData("<svg onload=alert(1)></svg>")]
    [InlineData("<iframe src=\"javascript:alert(1)\"></iframe>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<body onpageshow=alert(1)>")]
    public void Strips_active_content(string hostile)
    {
        var clean = _sanitizer.Sanitize(hostile) ?? "";
        Assert.DoesNotContain("alert", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", clean, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Keeps_benign_article_markup()
    {
        const string html = "<p>Hello <strong>world</strong></p><img src=\"https://ex.test/a.png\" loading=\"lazy\">";
        var clean = _sanitizer.Sanitize(html) ?? "";
        Assert.Contains("<strong>world</strong>", clean);
        Assert.Contains("https://ex.test/a.png", clean);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Passes_through_null_or_empty(string? input) => Assert.Equal(input, _sanitizer.Sanitize(input));

    [Fact]
    public void StripImages_removes_images_and_figures_but_keeps_text()
    {
        const string html =
            "<p>Lead text</p>" +
            "<figure><img src=\"https://ex.test/hero.jpg\"><figcaption>caption</figcaption></figure>" +
            "<p>More <strong>body</strong>.</p>";
        var clean = _sanitizer.Sanitize(html, stripImages: true) ?? "";
        Assert.DoesNotContain("<img", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<figure", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hero.jpg", clean);
        Assert.Contains("Lead text", clean);
        Assert.Contains("<strong>body</strong>", clean);
    }

    [Fact]
    public void StripImages_off_keeps_images()
    {
        const string html = "<p>x</p><img src=\"https://ex.test/a.png\">";
        var clean = _sanitizer.Sanitize(html, stripImages: false) ?? "";
        Assert.Contains("https://ex.test/a.png", clean);
    }
}
