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
            Summary: ToPlainText(summaryHtml),
            ContentHtml: contentHtml,
            ImageUrl: ExtractImage(item, contentHtml ?? summaryHtml),
            PublishedAt: published);
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

    private static string? ExtractImage(SyndicationItem item, string? html)
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

        // 3) first <img> in the content
        if (!string.IsNullOrEmpty(html))
        {
            var m = ImgSrcRegex().Match(html);
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
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
