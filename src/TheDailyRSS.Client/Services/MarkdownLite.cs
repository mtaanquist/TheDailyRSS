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
            sb.Append("<p>").Append(Inline(string.Join(" ", paragraph))).Append("</p>");
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

    /// <summary>Inline formatting on already-escaped text: **bold**, *italic*, `code`.</summary>
    private static string Inline(string text)
    {
        var escaped = WebUtility.HtmlEncode(text);
        escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", "<em>$1</em>");
        escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"`(.+?)`", "<code>$1</code>");
        return escaped;
    }
}
