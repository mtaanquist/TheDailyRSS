using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TheDailyRSS.Server.Services;

/// <summary>Parses raw RSS/Atom XML into <see cref="ParsedFeed"/> using SyndicationFeed.</summary>
public sealed partial class FeedReader
{
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";
    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";

    public ParsedFeed Parse(Stream xml, string feedUrl)
    {
        using var reader = XmlReader.Create(xml, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            MaxCharactersFromEntities = 1024,
            CloseInput = true,
        });

        var feed = SyndicationFeed.Load(reader)
                   ?? throw new InvalidOperationException("Not a recognizable RSS/Atom feed.");

        var siteUrl = feed.Links
            .FirstOrDefault(l => l.RelationshipType is null or "alternate")?.Uri?.ToString()
            ?? feed.Links.FirstOrDefault()?.Uri?.ToString();

        var items = feed.Items.Select(ParseItem).Where(i => i is not null).Cast<ParsedItem>().ToList();

        return new ParsedFeed(
            Title: string.IsNullOrWhiteSpace(feed.Title?.Text) ? "Untitled feed" : feed.Title!.Text.Trim(),
            SiteUrl: siteUrl,
            Items: items);
    }

    private static ParsedItem? ParseItem(SyndicationItem item)
    {
        var url = item.Links.FirstOrDefault(l => l.RelationshipType is null or "alternate")?.Uri?.ToString()
                  ?? item.Links.FirstOrDefault()?.Uri?.ToString()
                  ?? item.Id;
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var externalId = !string.IsNullOrWhiteSpace(item.Id) ? item.Id : url;

        var published = item.PublishDate != default ? item.PublishDate
            : item.LastUpdatedTime != default ? item.LastUpdatedTime
            : DateTimeOffset.UtcNow;

        var summaryHtml = item.Summary?.Text;
        var contentHtml = (item.Content as TextSyndicationContent)?.Text
                          ?? ReadExtension(item, Content + "encoded")
                          ?? summaryHtml;

        var author = item.Authors.FirstOrDefault()?.Name
                     ?? item.Authors.FirstOrDefault()?.Email;

        return new ParsedItem(
            ExternalId: externalId.Trim(),
            Title: string.IsNullOrWhiteSpace(item.Title?.Text) ? "(untitled)" : item.Title!.Text.Trim(),
            Url: url,
            Author: author,
            Summary: BestSummary(summaryHtml, contentHtml),
            ContentHtml: contentHtml,
            ImageUrl: ExtractImage(item, contentHtml, summaryHtml),
            PublishedAt: published);
    }

    /// <summary>The teaser text. Prefers the description, but some feeds stuff a bare image URL
    /// (or nothing) in there — fall back to the article body so the blurb isn't just a link.</summary>
    private static string? BestSummary(string? summaryHtml, string? contentHtml)
    {
        var text = ToPlainText(summaryHtml);
        if (string.IsNullOrWhiteSpace(text) || IsBareUrl(text))
            text = ToPlainText(contentHtml);
        return text;
    }

    private static string? ReadExtension(SyndicationItem item, XName name)
    {
        foreach (var ext in item.ElementExtensions)
        {
            var el = ext.GetObject<XElement>();
            if (el.Name == name)
                return el.Value;
        }
        return null;
    }

    private static string? ExtractImage(SyndicationItem item, string? contentHtml, string? summaryHtml)
    {
        // 1) media:content / media:thumbnail
        foreach (var ext in item.ElementExtensions)
        {
            var el = ext.GetObject<XElement>();
            if ((el.Name == Media + "content" || el.Name == Media + "thumbnail"))
            {
                var u = el.Attribute("url")?.Value;
                if (!string.IsNullOrWhiteSpace(u)) return u;
            }
        }

        // 2) enclosure of an image type
        var enclosure = item.Links.FirstOrDefault(l =>
            l.RelationshipType == "enclosure" && (l.MediaType?.StartsWith("image") ?? false));
        if (enclosure?.Uri is not null) return enclosure.Uri.ToString();

        // 3) first <img> in the content, then the description
        foreach (var html in new[] { contentHtml, summaryHtml })
        {
            if (string.IsNullOrEmpty(html)) continue;
            var m = ImgSrcRegex().Match(html);
            if (m.Success) return m.Groups[1].Value;
        }

        // 4) a description that is itself just an image URL (e.g. SønderborgNYT)
        var bare = ToPlainText(summaryHtml);
        if (IsImageUrl(bare)) return bare!.Trim();

        return null;
    }

    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".bmp"];

    /// <summary>True when the text is a single absolute http(s) URL and nothing else.</summary>
    private static bool IsBareUrl(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && !text.Trim().Contains(' ')
        && Uri.TryCreate(text.Trim(), UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    /// <summary>True when the text is a bare URL whose path ends in a known image extension.</summary>
    private static bool IsImageUrl(string? text)
    {
        if (!IsBareUrl(text)) return false;
        var path = text!.Trim();
        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];
        return ImageExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Strips tags &amp; collapses whitespace for the preview teaser.</summary>
    public static string? ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var text = TagRegex().Replace(html, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex("""<img[^>]+src\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ImgSrcRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
