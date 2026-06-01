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
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    /// <summary>Field elements we already lift into normalised columns — don't double-store
    /// them under <see cref="ParsedItem.Fields"/>.</summary>
    private static readonly HashSet<XName> ExcludedFieldNames = new()
    {
        Media + "content", Media + "thumbnail", Media + "description",
        Content + "encoded",
    };

    private const int MaxFieldValuesPerItem = 32;
    private const int MaxFieldValueLength = 256;

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
            Url: CleanUrl(url),
            Author: author,
            Summary: BestSummary(summaryHtml, contentHtml),
            ContentHtml: contentHtml,
            ImageUrl: ExtractImage(item, contentHtml, summaryHtml),
            PublishedAt: published,
            Fields: ExtractFields(item));
    }

    /// <summary>Builds the structured-field map used by the field-filter feature.
    /// Keys are lower-cased; values are lower-cased and trimmed; the whole map is capped so a
    /// pathological feed can't bloat the JSONB column.</summary>
    private static Dictionary<string, List<string>> ExtractFields(SyndicationItem item)
    {
        var fields = new Dictionary<string, List<string>>();
        var total = 0;

        // 1) Standard RSS/Atom categories.
        foreach (var c in item.Categories)
            if (!string.IsNullOrWhiteSpace(c.Name))
                AddField(fields, "category", c.Name, ref total);

        // 2) Authors (names and email-style identifiers feeds commonly use).
        foreach (var p in item.Authors)
        {
            if (!string.IsNullOrWhiteSpace(p.Name)) AddField(fields, "author", p.Name, ref total);
            if (!string.IsNullOrWhiteSpace(p.Email)) AddField(fields, "author", p.Email, ref total);
        }

        // 3) Element extensions in known and custom namespaces.
        foreach (var ext in item.ElementExtensions)
        {
            var el = ext.GetObject<XElement>();
            if (ExcludedFieldNames.Contains(el.Name)) continue;

            if (el.Name == Media + "keywords")
            {
                // media:keywords is a single comma-separated string in the spec.
                foreach (var k in (el.Value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    AddField(fields, "media:keywords", k, ref total);
                continue;
            }

            if (el.Name == Media + "category")
            {
                AddField(fields, "media:category", el.Value, ref total);
                continue;
            }

            if (el.Name.Namespace == Dc)
            {
                // dc:creator, dc:subject, etc. — leaf text only.
                if (IsLeafText(el))
                    AddField(fields, "dc:" + el.Name.LocalName, el.Value, ref total);
                continue;
            }

            // Other custom-namespace leaves: store under "{prefix-or-ns-host}:{localname}".
            if (el.Name.Namespace != XNamespace.None && IsLeafText(el))
            {
                var prefix = PrefixFor(el);
                if (prefix is null) continue;
                AddField(fields, prefix + ":" + el.Name.LocalName, el.Value, ref total);
            }
        }

        return fields;
    }

    private static void AddField(Dictionary<string, List<string>> fields, string key, string? value, ref int total)
    {
        if (total >= MaxFieldValuesPerItem) return;
        if (string.IsNullOrWhiteSpace(value)) return;
        var trimmed = value.Trim();
        if (trimmed.Length > MaxFieldValueLength) trimmed = trimmed[..MaxFieldValueLength];
        var k = key.ToLowerInvariant();
        var v = trimmed.ToLowerInvariant();
        if (!fields.TryGetValue(k, out var list)) fields[k] = list = new List<string>();
        // De-dupe within a single item so editions don't list "category: guides" three times.
        if (!list.Contains(v))
        {
            list.Add(v);
            total++;
        }
    }

    private static bool IsLeafText(XElement el) =>
        !el.HasElements && !string.IsNullOrWhiteSpace(el.Value);

    /// <summary>Best-effort prefix for a custom-namespace element so the filter key is
    /// human-readable in the UI. Prefers the in-document prefix; falls back to the namespace's
    /// host part. Returns null if neither is usable, so the field is skipped.</summary>
    private static string? PrefixFor(XElement el)
    {
        var ns = el.Name.Namespace;
        // GetPrefixOfNamespace walks up the ancestor chain to find any xmlns:foo declaration.
        var prefix = el.GetPrefixOfNamespace(ns);
        if (!string.IsNullOrWhiteSpace(prefix)) return prefix;
        if (!Uri.TryCreate(ns.NamespaceName, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host)) return null;
        // "search.yahoo.com" → "yahoo" is friendlier than the whole host; take the second-level label.
        var parts = host.Split('.');
        return parts.Length >= 2 ? parts[^2] : host;
    }

    // Query-string keys that are pure analytics/tracking noise. Prefixes catch families
    // (utm_*, at_* on BBC, ns_* on Guardian/CNN, mc_* on Mailchimp, pk_*/piwik on Matomo);
    // the set catches the well-known one-offs. Unknown params are kept so we never break a
    // link that genuinely needs them for routing.
    private static readonly string[] TrackingPrefixes =
        ["utm_", "at_", "ns_", "mc_", "pk_", "piwik_", "_hs", "hsa_", "wt_", "wt."];

    private static readonly HashSet<string> TrackingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "gclsrc", "dclid", "msclkid", "yclid", "ttclid", "twclid",
        "igshid", "mc_cid", "mc_eid", "ref", "ref_src", "referrer", "cmpid", "cmp",
        "icid", "ncid", "ito", "spm", "vero_id", "vero_conv", "s_kwcid", "guccounter",
        "__twitter_impression", "scrolla", "guce_referrer", "guce_referrer_sig",
    };

    private static bool IsTrackingParam(string key) =>
        TrackingKeys.Contains(key)
        || TrackingPrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Strips known tracking query parameters (e.g. <c>?at_medium=RSS&amp;at_campaign=rss</c>)
    /// from an article link before it's stored, so "read at source" points at a clean URL. Leaves the
    /// path, fragment and any non-tracking params untouched; returns the input unchanged if it isn't an
    /// absolute URL or has no query.</summary>
    public static string CleanUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Query.Length <= 1)
            return url;

        var kept = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair =>
            {
                var eq = pair.IndexOf('=');
                var key = Uri.UnescapeDataString(eq >= 0 ? pair[..eq] : pair);
                return !IsTrackingParam(key);
            })
            .ToList();

        var query = kept.Count > 0 ? "?" + string.Join('&', kept) : "";
        return uri.GetLeftPart(UriPartial.Path) + query + uri.Fragment;
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
