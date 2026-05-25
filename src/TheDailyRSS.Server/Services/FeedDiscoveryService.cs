using System.Text.RegularExpressions;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Services;

/// <summary>
/// Turns a pasted URL (site or feed) into an actual feed: parses it directly if it
/// is a feed, otherwise scrapes the page for an &lt;link rel="alternate"&gt; feed.
/// </summary>
public sealed partial class FeedDiscoveryService(IHttpClientFactory httpFactory, FeedReader reader, ILogger<FeedDiscoveryService> log)
{
    private HttpClient Http => httpFactory.CreateClient("feeds");

    public async Task<FeedDetectResult> DetectAsync(string url, CancellationToken ct, bool discover = true)
    {
        url = Normalize(url);
        try
        {
            // Try the URL as a feed first.
            var (feedUrl, parsed) = await ResolveAsync(url, ct, discover);
            if (parsed is null)
                return new FeedDetectResult(false, null, null, null, null, []);

            return new FeedDetectResult(
                Found: true,
                FeedUrl: feedUrl,
                Title: parsed.Title,
                SiteUrl: parsed.SiteUrl ?? url,
                IconText: IconText.From(parsed.Title),
                RecentHeadlines: parsed.Items.Take(3).Select(i => i.Title).ToArray());
        }
        catch (Exception ex)
        {
            log.LogInformation(ex, "Feed detection failed for {Url}", url);
            return new FeedDetectResult(false, null, null, null, null, []);
        }
    }

    /// <summary>Returns the resolved feed URL and its parsed contents, or (url, null) if none.
    /// When <paramref name="discover"/> is false, the URL is used exactly: if it isn't a feed
    /// itself we don't fall back to scraping the page for an advertised (often site-wide) feed.</summary>
    public async Task<(string FeedUrl, ParsedFeed? Feed)> ResolveAsync(string url, CancellationToken ct, bool discover = true)
    {
        url = Normalize(url);
        var (body, contentType) = await GetAsync(url, ct);

        if (LooksLikeFeed(body, contentType))
            return (url, ParseSafe(body, url));

        if (!discover)
            return (url, null);

        // Treat as HTML: find an alternate feed link.
        foreach (var href in FindFeedLinks(body))
        {
            var abs = ToAbsolute(url, href);
            try
            {
                var (feedBody, _) = await GetAsync(abs, ct);
                var parsed = ParseSafe(feedBody, abs);
                if (parsed is not null)
                    return (abs, parsed);
            }
            catch (Exception ex)
            {
                log.LogDebug(ex, "Candidate feed link {Href} failed", abs);
            }
        }

        return (url, null);
    }

    private async Task<(byte[] Body, string? ContentType)> GetAsync(string url, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        return (bytes, resp.Content.Headers.ContentType?.MediaType);
    }

    private ParsedFeed? ParseSafe(byte[] body, string url)
    {
        try
        {
            using var ms = new MemoryStream(body);
            return reader.Parse(ms, url);
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeFeed(byte[] body, string? contentType)
    {
        if (contentType is not null &&
            (contentType.Contains("xml") || contentType.Contains("rss") || contentType.Contains("atom")))
            return true;

        // Sniff the first bytes for a feed root element.
        var head = System.Text.Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 512));
        return head.Contains("<rss", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<feed", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<rdf:RDF", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> FindFeedLinks(byte[] body)
    {
        var html = System.Text.Encoding.UTF8.GetString(body);
        foreach (Match m in LinkTagRegex().Matches(html))
        {
            var tag = m.Value;
            if (!tag.Contains("alternate", StringComparison.OrdinalIgnoreCase)) continue;
            if (!tag.Contains("rss+xml", StringComparison.OrdinalIgnoreCase) &&
                !tag.Contains("atom+xml", StringComparison.OrdinalIgnoreCase)) continue;

            var href = HrefRegex().Match(tag);
            if (href.Success) yield return href.Groups[1].Value;
        }
    }

    private static string Normalize(string url)
    {
        url = url.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        return url;
    }

    private static string ToAbsolute(string baseUrl, string href) =>
        Uri.TryCreate(new Uri(baseUrl), href, out var abs) ? abs.ToString() : href;

    [GeneratedRegex("<link\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTagRegex();

    [GeneratedRegex("""href\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex HrefRegex();
}

/// <summary>Derives the 1–2 character source badge shown in the UI.</summary>
public static class IconText
{
    public static string From(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "?";
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return "?";
        if (words.Length == 1) return words[0][..1].ToUpperInvariant();
        return (char.ToUpperInvariant(words[0][0]).ToString() + char.ToUpperInvariant(words[1][0])).Trim();
    }
}
