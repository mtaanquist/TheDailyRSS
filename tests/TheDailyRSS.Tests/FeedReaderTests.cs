using System.Text;
using TheDailyRSS.Server.Endpoints;
using TheDailyRSS.Server.Services;
using Xunit;

namespace TheDailyRSS.Tests;

public class FeedReaderTests
{
    private static Stream S(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));

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
