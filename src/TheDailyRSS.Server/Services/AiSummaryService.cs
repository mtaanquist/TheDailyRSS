using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;
using static TheDailyRSS.Server.Services.ArticleQueries;

namespace TheDailyRSS.Server.Services;

/// <summary>Raised when a BYOK summary can't be produced (misconfiguration or upstream LLM error).
/// The endpoint surfaces <see cref="Exception.Message"/> to the client.</summary>
public sealed class AiException(string message) : Exception(message);

/// <summary>Generates and caches per-user AI digests of an edition (daily) or week (weekly),
/// calling the user's own OpenAI-compatible endpoint with their key.</summary>
public sealed class AiSummaryService(
    AppDbContext db,
    IHttpClientFactory httpFactory,
    IDataProtectionProvider dpProvider,
    ILogger<AiSummaryService> log)
{
    private const int MaxArticles = 150;
    private const int MaxSummaryChars = 400;
    private IDataProtector Protector => dpProvider.CreateProtector("AiApiKey");

    /// <summary>The Monday-anchored 7-day window containing <paramref name="date"/>.</summary>
    public static (DateOnly Start, DateOnly End) WeekRange(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
        var monday = date.AddDays(-offset);
        return (monday, monday.AddDays(6));
    }

    public string Encrypt(string apiKey) => Protector.Protect(apiKey);

    public async Task<AiSummaryDto?> GetCachedAsync(
        Guid uid, AiSummaryKind kind, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var row = await db.AiSummaries.FirstOrDefaultAsync(
            s => s.UserId == uid && s.Kind == kind && s.PeriodStart == start && s.PeriodEnd == end, ct);
        return row is null ? null : ToDto(row);
    }

    /// <summary>Generates (or regenerates) and caches a digest for the period. Throws
    /// <see cref="AiException"/> when AI isn't configured or the upstream call fails.</summary>
    public async Task<AiSummaryDto> GenerateAsync(
        AppUser user, AiSummaryKind kind, DateOnly start, DateOnly end, CancellationToken ct)
    {
        if (!user.AiEnabled)
            throw new AiException("AI summaries are turned off. Enable them in settings.");
        if (string.IsNullOrWhiteSpace(user.AiBaseUrl) || string.IsNullOrWhiteSpace(user.AiModel)
            || string.IsNullOrWhiteSpace(user.AiApiKeyEncrypted))
            throw new AiException("AI summaries aren't fully configured. Add an endpoint, model and API key in settings.");

        var corpus = await BuildCorpusAsync(user.Id, start, end, ct);
        if (corpus.ArticleCount == 0)
            throw new AiException("There are no articles in this period to summarise.");

        var content = await CallLlmAsync(user, kind, start, end, corpus.Text, ct);

        var existing = await db.AiSummaries.FirstOrDefaultAsync(
            s => s.UserId == user.Id && s.Kind == kind && s.PeriodStart == start && s.PeriodEnd == end, ct);
        if (existing is null)
        {
            existing = new AiSummary { UserId = user.Id, Kind = kind, PeriodStart = start, PeriodEnd = end };
            db.AiSummaries.Add(existing);
        }
        existing.Content = content;
        existing.Model = user.AiModel!;
        existing.ArticleCount = corpus.ArticleCount;
        existing.GeneratedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return ToDto(existing);
    }

    private async Task<(int ArticleCount, string Text)> BuildCorpusAsync(
        Guid uid, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var filters = await LoadFiltersAsync(db, uid, ct);
        var visible = NotHidden(ApplyKeywords(Subscribed(db, uid), filters), uid)
            .Where(a => a.EditionDate >= start && a.EditionDate <= end);

        var items = await (
            from a in visible
            from sub in db.Subscriptions.Where(s => s.UserId == uid && s.SourceId == a.SourceId)
            orderby a.PublishedAt descending
            select new
            {
                a.Title,
                a.Summary,
                Source = sub.CustomTitle ?? a.Source!.Title,
                Category = sub.Category!.Name,
                CategoryOrder = sub.Category.SortOrder,
            })
            .Take(MaxArticles)
            .ToListAsync(ct);

        if (items.Count == 0) return (0, "");

        var sb = new StringBuilder();
        foreach (var group in items.GroupBy(i => (i.Category, i.CategoryOrder)).OrderBy(g => g.Key.CategoryOrder))
        {
            sb.Append("## ").AppendLine(group.Key.Category);
            foreach (var i in group)
            {
                sb.Append("- [").Append(i.Source).Append("] ").Append(i.Title);
                var summary = Strip(i.Summary);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    if (summary.Length > MaxSummaryChars) summary = summary[..MaxSummaryChars] + "…";
                    sb.Append(" — ").Append(summary);
                }
                sb.AppendLine();
            }
            sb.AppendLine();
        }
        return (items.Count, sb.ToString());
    }

    private async Task<string> CallLlmAsync(
        AppUser user, AiSummaryKind kind, DateOnly start, DateOnly end, string corpus, CancellationToken ct)
    {
        var apiKey = DecryptKey(user.AiApiKeyEncrypted!);
        var http = httpFactory.CreateClient("ai");

        var period = kind == AiSummaryKind.Weekly
            ? $"the week of {start:MMMM d} – {end:MMMM d, yyyy}"
            : $"{start:dddd, MMMM d, yyyy}";

        var system = new StringBuilder();
        system.Append("You are a news editor writing a personal briefing for a reader. ")
            .Append("Summarise the day's stories below into a tight, scannable digest in Markdown. ")
            .Append("Group related stories, lead with what matters most to this reader, and keep it concise. ")
            .Append("Do not invent facts beyond the provided headlines and blurbs.");
        if (!string.IsNullOrWhiteSpace(user.AiSystemPrompt))
            system.Append("\n\nThe reader describes their interests as follows; weight the briefing accordingly:\n")
                .Append(user.AiSystemPrompt!.Trim());

        var userMsg = $"Here are the articles from {period}. Write the briefing.\n\n{corpus}";

        var payload = new ChatRequest(
            user.AiModel!,
            [new ChatMessage("system", system.ToString()), new ChatMessage("user", userMsg)],
            0.4);

        using var req = new HttpRequestMessage(HttpMethod.Post, CombineUrl(user.AiBaseUrl!, "chat/completions"))
        {
            Content = JsonContent.Create(payload),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "AI request failed for user {UserId}", user.Id);
            throw new AiException("Couldn't reach the AI endpoint. Check the base URL and your network.");
        }

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            log.LogWarning("AI endpoint returned {Status} for user {UserId}: {Body}", resp.StatusCode, user.Id, Truncate(body, 500));
            var hint = resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ? " Check your API key."
                : resp.StatusCode == System.Net.HttpStatusCode.NotFound ? " Check the base URL and model."
                : "";
            throw new AiException($"The AI endpoint returned an error ({(int)resp.StatusCode}).{hint}");
        }

        ChatResponse? parsed;
        try { parsed = await resp.Content.ReadFromJsonAsync<ChatResponse>(ct); }
        catch (JsonException) { throw new AiException("The AI endpoint returned an unexpected response."); }

        var text = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(text))
            throw new AiException("The AI endpoint returned an empty response.");
        return text.Trim();
    }

    private string DecryptKey(string encrypted)
    {
        try { return Protector.Unprotect(encrypted); }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not decrypt stored AI key");
            throw new AiException("Your stored API key couldn't be read. Please re-enter it in settings.");
        }
    }

    private static AiSummaryDto ToDto(AiSummary s) =>
        new(s.Kind, s.PeriodStart, s.PeriodEnd, s.Content, s.Model, s.ArticleCount, s.GeneratedAt);

    private static string CombineUrl(string baseUrl, string path) =>
        $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>Crude HTML/whitespace strip so feed summaries read cleanly in the prompt.</summary>
    private static string Strip(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }

    // ── OpenAI-compatible chat-completions shapes ───────────────────────
    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatMessage? Message);
}
