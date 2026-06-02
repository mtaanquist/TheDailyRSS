using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Services;

namespace TheDailyRSS.Server.Endpoints;

/// <summary>The anonymous, server-rendered public page for a shared article (<c>/share/{token}</c>).
///
/// <para>This is deliberately NOT a Blazor page. Link unfurlers (Discord, Telegram, Slack, iMessage)
/// don't run JavaScript — they read the raw HTML's Open Graph tags — and the WASM app would also
/// bounce a logged-out friend to /login. So the page is rendered to plain HTML here on the server.</para>
///
/// <para>It resolves only existing, non-revoked <see cref="SharedArticle"/> tokens (articles are private
/// until explicitly shared) and shows just the masthead + article: no sidebar, and nothing tied to the
/// reader who shared it (no name, email, read/saved state or AI summary).</para></summary>
public static class ShareEndpoints
{
    public static void MapShareEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous on purpose — no RequireAuthorization. Must be mapped before MapFallbackToFile.
        app.MapGet("/share/{token:guid}", GetSharePage);
    }

    private static async Task<IResult> GetSharePage(
        Guid token, HttpContext http, AppDbContext db, HtmlSanitizationService sanitizer, CancellationToken ct)
    {
        // Instance-wide kill switch: when an admin turns sharing off, existing links stop resolving too.
        if (await SiteSettings.IsSharingDisabledAsync(db, ct)) return Results.NotFound();

        // Resolve the token to its article. Select only non-personal columns and use the canonical
        // source title (not a subscription's CustomTitle) — there is no reader context on a public page.
        var row = await db.SharedArticles
            .Where(s => s.Id == token && s.RevokedAt == null)
            .Select(s => new
            {
                s.Article!.Title,
                s.Article.Author,
                s.Article.Summary,
                s.Article.ContentHtml,
                s.Article.FullContentHtml,
                FetchFull = s.Article.Source!.FetchFullContent,
                FeedTitle = s.Article.Source.Title,
                s.Article.ImageUrl,
                s.Article.PublishedAt,
                s.Article.Url,
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return Results.NotFound();

        // Mirror the reading pane: prefer the reader-mode extraction when the source has it and we have a
        // usable body, else the feed's own content. Sanitized either way (server-side XSS allowlist).
        var useFull = row.FetchFull && !string.IsNullOrEmpty(row.FullContentHtml);
        var bodyHtml = sanitizer.Sanitize(useFull ? row.FullContentHtml : row.ContentHtml, stripImages: useFull) ?? "";

        var pageUrl = $"{http.Request.Scheme}://{http.Request.Host}/share/{token}";
        var html = RenderPage(row.Title, row.Author, row.Summary, row.FeedTitle,
            row.ImageUrl, row.PublishedAt, row.Url, bodyHtml, pageUrl);
        return Results.Content(html, "text/html; charset=utf-8");
    }

    /// <summary>Builds the full standalone HTML document. Plain-text values are HTML-encoded;
    /// <paramref name="bodyHtml"/> is already sanitized and emitted as markup.</summary>
    private static string RenderPage(
        string title, string? author, string? summary, string feedTitle,
        string? imageUrl, DateTimeOffset publishedAt, string sourceUrl, string bodyHtml, string pageUrl)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

        var titleEnc = E(title);
        var feedEnc = E(feedTitle);
        var dateText = publishedAt.ToString("dddd, MMMM d, yyyy · HH:mm 'UTC'");
        var ogImage = SafeAbsoluteUrl(imageUrl);
        var description = Excerpt(summary, bodyHtml);
        var sourceHref = SafeAbsoluteUrl(sourceUrl);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n");
        // Theme attributes mirror wwwroot/index.html so css/app.css's variables resolve.
        sb.Append("<html lang=\"en\" data-theme=\"newsprint\" data-font=\"ptserif\" data-density=\"balanced\">\n");
        sb.Append("<head>\n");
        sb.Append("<meta charset=\"utf-8\" />\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0, viewport-fit=cover\" />\n");
        sb.Append($"<title>{titleEnc} · The Daily RSS</title>\n");
        sb.Append($"<link rel=\"canonical\" href=\"{E(pageUrl)}\" />\n");
        sb.Append("<meta name=\"theme-color\" content=\"#f4e9cf\" />\n");
        sb.Append("<link rel=\"icon\" type=\"image/svg+xml\" href=\"/favicon.svg\" />\n");
        // Open Graph / Twitter card — the payload link unfurlers read.
        sb.Append("<meta property=\"og:type\" content=\"article\" />\n");
        sb.Append("<meta property=\"og:site_name\" content=\"The Daily RSS\" />\n");
        sb.Append($"<meta property=\"og:title\" content=\"{titleEnc}\" />\n");
        sb.Append($"<meta property=\"og:description\" content=\"{E(description)}\" />\n");
        sb.Append($"<meta property=\"og:url\" content=\"{E(pageUrl)}\" />\n");
        if (ogImage is not null)
        {
            sb.Append($"<meta property=\"og:image\" content=\"{E(ogImage)}\" />\n");
            sb.Append("<meta name=\"twitter:card\" content=\"summary_large_image\" />\n");
        }
        else
        {
            sb.Append("<meta name=\"twitter:card\" content=\"summary\" />\n");
        }
        sb.Append($"<meta name=\"twitter:title\" content=\"{titleEnc}\" />\n");
        sb.Append($"<meta name=\"twitter:description\" content=\"{E(description)}\" />\n");
        // Fonts + design system, copied from index.html so the page matches the paper look.
        sb.Append("<link rel=\"preconnect\" href=\"https://fonts.googleapis.com\" />\n");
        sb.Append("<link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin />\n");
        sb.Append("<link href=\"https://fonts.googleapis.com/css2?family=Source+Serif+4:ital,opsz,wght@0,8..60,400;0,8..60,600;0,8..60,700;1,8..60,400&family=Newsreader:ital,opsz,wght@0,6..72,400;0,6..72,600;0,6..72,700;1,6..72,400&family=Lora:ital,wght@0,400;0,600;0,700;1,400&family=Inter:wght@400;500;600;700&display=swap\" rel=\"stylesheet\" />\n");
        sb.Append("<link rel=\"stylesheet\" href=\"/css/app.css\" />\n");
        sb.Append("</head>\n");
        sb.Append("<body>\n");
        sb.Append("<main class=\"tdr-share\">\n");

        // Masthead — a static version of Edition.razor's, without weather/issue/refresh.
        sb.Append("<header class=\"tdr-masthead\">\n");
        sb.Append("<h1 class=\"tdr-masthead-title\">The Daily RSS</h1>\n");
        sb.Append($"<div class=\"tdr-masthead-rule\"><span>— {feedEnc} —</span></div>\n");
        sb.Append("</header>\n");

        // Article — a static, read-only version of Article.razor's markup.
        sb.Append("<article class=\"tdr-article\">\n");
        sb.Append($"<div class=\"kicker\">{feedEnc}</div>\n");
        sb.Append($"<h1>{titleEnc}</h1>\n");
        sb.Append("<div class=\"byline\">\n");
        if (!string.IsNullOrWhiteSpace(author))
            sb.Append($"<span>By {E(author)}</span><span>·</span>\n");
        sb.Append($"<span style=\"font-style:italic\">{E(dateText)}</span>\n");
        sb.Append("</div>\n");
        if (ogImage is not null)
            sb.Append($"<div class=\"hero\"><img src=\"{E(ogImage)}\" alt=\"\" referrerpolicy=\"no-referrer\" /></div>\n");
        sb.Append("<div class=\"content\">\n");
        if (!string.IsNullOrWhiteSpace(bodyHtml))
            sb.Append(bodyHtml);
        else if (!string.IsNullOrWhiteSpace(summary))
            sb.Append($"<p>{E(summary)}</p>");
        else
            sb.Append("<p style=\"font-style:italic; color:var(--ink-2)\">No preview text — read it at the source.</p>");
        sb.Append("\n</div>\n");
        if (sourceHref is not null)
            sb.Append($"<div class=\"actions\"><a class=\"tdr-btn\" href=\"{E(sourceHref)}\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">read at source ↗</a></div>\n");
        sb.Append("</article>\n");

        sb.Append("</main>\n");
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>A short plain-text description for the embed: the feed summary if present, else the
    /// rendered body, with tags stripped and whitespace collapsed, truncated to ~200 chars.</summary>
    private static string Excerpt(string? summary, string bodyHtml)
    {
        var source = !string.IsNullOrWhiteSpace(summary) ? summary! : bodyHtml;
        var text = WebUtility.HtmlDecode(Regex.Replace(source, "<[^>]+>", " "));
        text = Regex.Replace(text, "\\s+", " ").Trim();
        return text.Length <= 200 ? text : text[..200].TrimEnd() + "…";
    }

    /// <summary>Only lets absolute http(s) URLs through — for the hero/og:image and the source link.
    /// Crawlers ignore relative og:image values, and a feed-supplied <c>javascript:</c> URL must never
    /// reach an href. Returns null when the URL is unusable.</summary>
    private static string? SafeAbsoluteUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
            ? url
            : null;
}
