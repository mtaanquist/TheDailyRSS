using Ganss.Xss;

namespace TheDailyRSS.Server.Services;

/// <summary>
/// Allowlist-based sanitizer for untrusted feed HTML. The previous defence was a client-side
/// regex that stripped only <c>&lt;script&gt;/&lt;style&gt;/&lt;iframe&gt;</c> — trivially bypassed
/// (<c>onerror=</c>, <c>javascript:</c>, <c>&lt;svg onload&gt;</c>, …). This runs a real HTML parser
/// (AngleSharp) and keeps only an explicit allowlist of tags/attributes, dropping every event handler
/// and dangerous URL scheme. Article bodies are rendered as raw markup in the client, so this is the
/// one place that keeps a hostile feed from running script in the app origin.
///
/// <para>Thread-safe to share as a singleton: the configuration is fixed after construction and
/// <see cref="HtmlSanitizer.Sanitize(string)"/> does not mutate it.</para>
/// </summary>
public sealed class HtmlSanitizationService
{
    private readonly HtmlSanitizer _sanitizer;

    public HtmlSanitizationService()
    {
        // HtmlSanitizer's defaults already block scripts, event handlers and javascript:/data: URLs,
        // and restrict schemes to http(s)/mailto. We start from those and only relax the couple of
        // attributes article content legitimately needs.
        _sanitizer = new HtmlSanitizer();
        _sanitizer.AllowedAttributes.Add("loading");        // lazy images
        _sanitizer.AllowedAttributes.Add("referrerpolicy"); // referrer-safe images
    }

    /// <summary>Returns sanitized HTML, or the input unchanged when it is null/empty.</summary>
    public string? Sanitize(string? html) =>
        string.IsNullOrEmpty(html) ? html : _sanitizer.Sanitize(html);
}
