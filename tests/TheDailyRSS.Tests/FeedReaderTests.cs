using System.Text;
using TheDailyRSS.Server.Endpoints;
using TheDailyRSS.Server.Services;
using Xunit;

namespace TheDailyRSS.Tests;

public class FeedReaderTests
{
    private static Stream S(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));

    /// <summary>Parses a single &lt;item&gt; wrapped in a standard RSS envelope (content/media/dc
    /// namespaces declared) so each test only has to spell out the part that matters.</summary>
    private static ParsedItem ParseItem(string itemXml)
    {
        var rss = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0"
             xmlns:content="http://purl.org/rss/1.0/modules/content/"
             xmlns:media="http://search.yahoo.com/mrss/"
             xmlns:dc="http://purl.org/dc/elements/1.1/">
          <channel>
            <title>Test</title>
            <link>https://example.test</link>
            <item>{itemXml}</item>
          </channel>
        </rss>
        """;
        return Assert.Single(new FeedReader().Parse(S(rss), "https://example.test/feed").Items);
    }

    private const string Head = "<title>T</title><link>https://example.test/a</link><guid>a</guid>";

    [Fact]
    public void Parses_Rss2_With_Content_And_Image()
    {
        const string rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/" xmlns:media="http://search.yahoo.com/mrss/">
          <channel>
            <title>The Verge</title>
            <link>https://www.theverge.com</link>
            <item>
              <title>A big story</title>
              <link>https://www.theverge.com/a-big-story</link>
              <guid>verge-1</guid>
              <pubDate>Mon, 25 May 2026 09:00:00 GMT</pubDate>
              <author>jane@verge.com</author>
              <description>&lt;p&gt;Short &lt;b&gt;teaser&lt;/b&gt; here.&lt;/p&gt;</description>
              <content:encoded>&lt;p&gt;Full body with &lt;img src="https://img.example/x.jpg" /&gt; inside.&lt;/p&gt;</content:encoded>
              <media:thumbnail url="https://img.example/thumb.jpg" />
            </item>
          </channel>
        </rss>
        """;

        var feed = new FeedReader().Parse(S(rss), "https://www.theverge.com/feed");

        Assert.Equal("The Verge", feed.Title);
        Assert.StartsWith("https://www.theverge.com", feed.SiteUrl); // SyndicationFeed may normalize a trailing slash
        var item = Assert.Single(feed.Items);
        Assert.Equal("A big story", item.Title);
        Assert.Equal("verge-1", item.ExternalId);
        Assert.Equal("https://www.theverge.com/a-big-story", item.Url);
        Assert.Equal("https://img.example/thumb.jpg", item.ImageUrl); // media:thumbnail wins
        Assert.Equal("Short teaser here.", item.Summary);             // tags stripped, decoded
        Assert.Contains("Full body", item.ContentHtml);
        Assert.Equal(new DateTimeOffset(2026, 5, 25, 9, 0, 0, TimeSpan.Zero), item.PublishedAt);
    }

    [Fact]
    public void Parses_Atom_And_Extracts_First_Img_When_No_Media()
    {
        const string atom = """
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <title>Quanta</title>
          <link rel="alternate" href="https://www.quantamagazine.org" />
          <entry>
            <title>Math proof</title>
            <link rel="alternate" href="https://quanta/math-proof" />
            <id>tag:quanta,2026:1</id>
            <updated>2026-05-24T12:00:00Z</updated>
            <content type="html">&lt;p&gt;Body &lt;img src='https://q/pic.png'&gt; more&lt;/p&gt;</content>
          </entry>
        </feed>
        """;

        var feed = new FeedReader().Parse(S(atom), "https://www.quantamagazine.org/feed");

        Assert.Equal("Quanta", feed.Title);
        var item = Assert.Single(feed.Items);
        Assert.Equal("Math proof", item.Title);
        Assert.Equal("https://quanta/math-proof", item.Url);
        Assert.Equal("https://q/pic.png", item.ImageUrl); // pulled from <img> in content
    }

    [Fact]
    public void Description_That_Is_A_Bare_Image_Url_Becomes_Image_And_Body_Becomes_Teaser()
    {
        // SønderborgNYT-style: <description> holds only an image URL, real text is in content:encoded.
        const string rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/" xmlns:dc="http://purl.org/dc/elements/1.1/">
          <channel>
            <title>SønderborgNYT</title>
            <link>https://www.sonderborgnyt.dk</link>
            <item>
              <title>Kasernens Dag</title>
              <link>https://www.sonderborgnyt.dk/kasernens-dag/</link>
              <guid isPermaLink="false">https://www.sonderborgnyt.dk/?p=381822</guid>
              <pubDate>Mon, 25 May 2026 11:08:13 +0000</pubDate>
              <description>
                https://www.sonderborgnyt.dk/wp-content/uploads/2026/05/Soenderborg-karserne_sonderborgnyt.jpg
              </description>
              <content:encoded>&lt;p&gt;Sønderborg Kommune inviterer til Kasernens Dag.&lt;/p&gt;&lt;p&gt;Alle er velkomne.&lt;/p&gt;</content:encoded>
            </item>
          </channel>
        </rss>
        """;

        var feed = new FeedReader().Parse(S(rss), "https://www.sonderborgnyt.dk/feed");
        var item = Assert.Single(feed.Items);

        Assert.Equal(
            "https://www.sonderborgnyt.dk/wp-content/uploads/2026/05/Soenderborg-karserne_sonderborgnyt.jpg",
            item.ImageUrl);
        Assert.StartsWith("Sønderborg Kommune inviterer", item.Summary);
        Assert.DoesNotContain("http", item.Summary); // the bare URL is no longer the blurb
    }

    // ── Image source priority ──────────────────────────────────────────
    // media:* > enclosure(image) > <img> in content > <img> in description > bare-URL description.

    [Fact]
    public void Image_prefers_media_over_enclosure_and_embedded_img()
    {
        var item = ParseItem($"""
            {Head}
            <enclosure url="https://img.test/enclosure.jpg" type="image/jpeg" length="1" />
            <media:content url="https://img.test/media.jpg" />
            <description><![CDATA[<p>Text <img src="https://img.test/incontent.jpg"/></p>]]></description>
            """);
        Assert.Equal("https://img.test/media.jpg", item.ImageUrl);
    }

    [Fact]
    public void Image_uses_enclosure_when_no_media()
    {
        var item = ParseItem($"""
            {Head}
            <enclosure url="https://img.test/enclosure.jpg" type="image/jpeg" length="1" />
            <description><![CDATA[<p>Just text, no image.</p>]]></description>
            """);
        Assert.Equal("https://img.test/enclosure.jpg", item.ImageUrl);
    }

    [Fact]
    public void Image_ignores_non_image_enclosure()
    {
        var item = ParseItem($"""
            {Head}
            <enclosure url="https://media.test/episode.mp3" type="audio/mpeg" length="1" />
            <description><![CDATA[<p>A podcast episode.</p>]]></description>
            """);
        Assert.Null(item.ImageUrl);
    }

    [Fact]
    public void Image_falls_back_to_img_in_description_when_content_has_none()
    {
        var item = ParseItem($"""
            {Head}
            <description><![CDATA[<p><img src="https://img.test/desc.jpg"/> caption</p>]]></description>
            <content:encoded><![CDATA[<p>Body without images.</p>]]></content:encoded>
            """);
        Assert.Equal("https://img.test/desc.jpg", item.ImageUrl);
        Assert.Equal("caption", item.Summary); // description has real text, so it stays the teaser
    }

    // ── Summary selection ───────────────────────────────────────────────

    [Fact]
    public void Text_description_is_kept_even_when_content_is_richer()
    {
        var item = ParseItem($"""
            {Head}
            <description><![CDATA[Short teaser.]]></description>
            <content:encoded><![CDATA[<p>Much longer body text goes here.</p>]]></content:encoded>
            """);
        Assert.Equal("Short teaser.", item.Summary);
    }

    [Fact]
    public void Description_with_a_url_among_words_is_not_mistaken_for_a_bare_url()
    {
        var item = ParseItem($"""
            {Head}
            <description><![CDATA[Read more at https://example.test today.]]></description>
            <content:encoded><![CDATA[<p>Body.</p>]]></content:encoded>
            """);
        Assert.StartsWith("Read more at", item.Summary);
    }

    [Fact]
    public void Bare_non_image_url_description_is_not_an_image_and_summary_falls_back_to_body()
    {
        var item = ParseItem($"""
            {Head}
            <description>https://example.test/some-article-link</description>
            <content:encoded><![CDATA[<p>The real story body.</p>]]></content:encoded>
            """);
        Assert.Null(item.ImageUrl);
        Assert.Equal("The real story body.", item.Summary);
    }

    [Fact]
    public void Missing_description_and_content_yields_null_summary_and_image()
    {
        var item = ParseItem(Head);
        Assert.Null(item.Summary);
        Assert.Null(item.ImageUrl);
    }

    [Theory]
    [InlineData("https://img.test/p.JPG")]      // case-insensitive extension
    [InlineData("https://img.test/p.png?w=600")] // query string allowed
    [InlineData("https://img.test/p.webp")]
    public void Bare_image_url_description_becomes_the_image(string url)
    {
        var item = ParseItem($"""
            {Head}
            <description>{url}</description>
            <content:encoded><![CDATA[<p>Body text.</p>]]></content:encoded>
            """);
        Assert.Equal(url, item.ImageUrl);
        Assert.Equal("Body text.", item.Summary);
    }

    [Fact]
    public void ToPlainText_Strips_And_Collapses()
    {
        Assert.Equal("Hello world", FeedReader.ToPlainText("<p>Hello   <b>world</b></p>\n"));
        Assert.Null(FeedReader.ToPlainText("   "));
    }

    [Theory]
    [InlineData("The New York Times", "TN")]
    [InlineData("BBC", "B")]
    [InlineData("", "?")]
    public void IconText_Derives_Badge(string title, string expected) =>
        Assert.Equal(expected, IconText.From(title));
}

public class MastheadTests
{
    [Fact]
    public void Formats_Date_Volume_Issue()
    {
        var d = new DateOnly(2026, 5, 25);
        Assert.Equal("MONDAY · MAY 25, 2026", Masthead.DateLabel(d));
        Assert.Equal("VOL. III", Masthead.Volume(d));   // 2026 - 2024 + 1 = 3 -> III
        Assert.StartsWith("NO. ", Masthead.Issue(d));
    }
}
