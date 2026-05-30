using System.Text;
using Microsoft.Extensions.Options;

namespace TheDailyRSS.Server.Services;

/// <summary>
/// Fetches an article's web page and runs a reader-mode (Readability) extraction over it, returning
/// the cleaned content HTML. Pure fetch+extract — it touches no database — so both the live fetch
/// path and the backfill worker can reuse it. Reuses the SSRF-guarded <c>"feeds"</c> HttpClient.
/// </summary>
public sealed class ArticleContentExtractor(
    IHttpClientFactory httpFactory,
    IOptions<FeedOptions> options,
    ILogger<ArticleContentExtractor> log)
{
    private readonly FeedOptions _options = options.Value;

    /// <summary>
    /// Returns the reader-mode content HTML for <paramref name="articleUrl"/>, or <c>null</c> when the
    /// page can't be fetched, isn't HTML, or yields nothing readable. Never throws for an expected
    /// failure — callers treat <c>null</c> as "fall back to the feed's own content".
    /// </summary>
    public async Task<string?> ExtractAsync(string articleUrl, CancellationToken ct)
    {
        try
        {
            var http = httpFactory.CreateClient("feeds");
            using var resp = await http.GetAsync(articleUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogDebug("Full-content fetch for {Url} returned {Status}", articleUrl, (int)resp.StatusCode);
                return null;
            }

            // Only attempt extraction on HTML; skip PDFs, images, JSON, etc.
            var mediaType = resp.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null &&
                mediaType is not "text/html" and not "application/xhtml+xml")
            {
                log.LogDebug("Full-content fetch for {Url} is {MediaType}, not HTML", articleUrl, mediaType);
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var buffered = await HttpStreamUtil.ReadCappedAsync(stream, _options.MaxResponseBytes, ct);

            var encoding = ResolveEncoding(resp.Content.Headers.ContentType?.CharSet);
            var html = encoding.GetString(buffered.GetBuffer(), 0, (int)buffered.Length);

            // SmartReader does its own HTTP if handed only a URL; the pre-fetched-HTML constructor keeps
            // the request on our SSRF-guarded, size-capped client. The URL is still needed so relative
            // links and images resolve to absolute.
            var reader = new SmartReader.Reader(articleUrl, html);
            var article = reader.GetArticle();
            if (!article.IsReadable || string.IsNullOrWhiteSpace(article.Content))
            {
                log.LogDebug("No readable content extracted from {Url}", articleUrl);
                return null;
            }

            return article.Content;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Full-content extraction failed for {Url}", articleUrl);
            return null;
        }
    }

    private static Encoding ResolveEncoding(string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { return Encoding.GetEncoding(charset.Trim('"', '\'')); }
            catch (ArgumentException) { /* unknown charset — fall through to UTF-8 */ }
        }
        return Encoding.UTF8;
    }
}
