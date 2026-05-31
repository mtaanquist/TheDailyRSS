using System.Net;
using System.Text;

namespace TheDailyRSS.Client.Services;

/// <summary>A tiny, safe Markdown→HTML converter for AI digest content. It HTML-escapes the input
/// first, then applies a limited subset (headings, bold/italic, bullet &amp; numbered lists,
/// paragraphs), so raw HTML in the model output can never inject markup.</summary>
public static class MarkdownLite
{
    public static string ToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var listType = ""; // "ul" | "ol" | ""
        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            sb.Append("<p>").Append(BreakBeforeLeadIns(Inline(string.Join(" ", paragraph)))).Append("</p>");
            paragraph.Clear();
        }
        void CloseList()
        {
            if (listType.Length == 0) return;
            sb.Append(listType == "ol" ? "</ol>" : "</ul>");
            listType = "";
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.Length == 0) { FlushParagraph(); CloseList(); continue; }

            // Headings: #, ##, ###
            var hashes = 0;
            while (hashes < trimmed.Length && trimmed[hashes] == '#') hashes++;
            if (hashes is >= 1 and <= 4 && hashes < trimmed.Length && trimmed[hashes] == ' ')
            {
                FlushParagraph(); CloseList();
                var level = Math.Min(hashes + 1, 4); // demote so digest headings sit under page chrome
                var text = Inline(trimmed[(hashes + 1)..].Trim());
                sb.Append("<h").Append(level).Append('>').Append(text).Append("</h").Append(level).Append('>');
                continue;
            }

            // Bullet list: -, *, •
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("• "))
            {
                FlushParagraph();
                if (listType != "ul") { CloseList(); sb.Append("<ul>"); listType = "ul"; }
                sb.Append("<li>").Append(Inline(trimmed[2..].Trim())).Append("</li>");
                continue;
            }

            // Numbered list: "1. "
            var dot = trimmed.IndexOf(". ", StringComparison.Ordinal);
            if (dot is > 0 and <= 3 && trimmed[..dot].All(char.IsDigit))
            {
                FlushParagraph();
                if (listType != "ol") { CloseList(); sb.Append("<ol>"); listType = "ol"; }
                sb.Append("<li>").Append(Inline(trimmed[(dot + 2)..].Trim())).Append("</li>");
                continue;
            }

            CloseList();
            paragraph.Add(trimmed);
        }

        FlushParagraph();
        CloseList();
        return sb.ToString();
    }

    /// <summary>Puts a line break before each in-paragraph bold "lead-in:" after the first, so a briefing
    /// the model wrote as one running paragraph of topics still reads one-topic-per-line. Matches only a bold
    /// run terminated by a colon (the lead-in shape) that sits mid-paragraph (preceded by other text), so a
    /// leading lead-in and ordinary mid-sentence bold are left alone; the intended bullet-list format, which
    /// renders as &lt;li&gt; rather than &lt;p&gt;, never reaches here.</summary>
    private static string BreakBeforeLeadIns(string html) =>
        System.Text.RegularExpressions.Regex.Replace(
            html, @"(?<=\S)\s+(<strong>[^<]*:</strong>|<strong>[^<]*</strong>:)", "<br>$1");

    /// <summary>Inline formatting on already-escaped text: [text](url) links, **bold**, *italic*, `code`.</summary>
    private static string Inline(string text)
    {
        var escaped = WebUtility.HtmlEncode(text);
        // Links first, and only http(s) targets — the URL is matched literally so the bold/italic passes
        // below can't chew it up, and a `javascript:`/`data:` href can never be produced. (Input is already
        // HTML-encoded, so an `&` in a query string is already `&amp;`, which is valid inside href.)
        escaped = System.Text.RegularExpressions.Regex.Replace(
            escaped, @"\[([^\]]+)\]\((https?://[^\s)]+)\)",
            m => $"<a href=\"{m.Groups[2].Value}\" target=\"_blank\" rel=\"noopener noreferrer\">{m.Groups[1].Value}</a>");
        escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", "<em>$1</em>");
        escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"`(.+?)`", "<code>$1</code>");
        return escaped;
    }
}
