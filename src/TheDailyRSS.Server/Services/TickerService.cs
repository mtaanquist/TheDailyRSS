using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Services;

/// <summary>Fetches stock/index quotes and symbol-search suggestions from Yahoo Finance's free, key-less
/// JSON endpoints, via <see cref="ExternalApiClient"/> (which gives it timeouts, a size cap and
/// graceful "return null on failure" semantics). Pure HTTP + mapping — persistence is the caller's job —
/// so it's a stateless singleton.</summary>
public sealed class TickerService(ExternalApiClient api, ILogger<TickerService> log)
{
    private const string ChartBase = "https://query1.finance.yahoo.com/v8/finance/chart";
    private const string SearchBase = "https://query1.finance.yahoo.com/v1/finance/search";

    /// <summary>Canonical storage form for a symbol: trimmed and upper-cased.</summary>
    public static string Normalize(string symbol) => symbol.Trim().ToUpperInvariant();

    /// <summary>The latest quote for a symbol, or null if it doesn't resolve or the call failed.</summary>
    public async Task<TickerQuote?> FetchQuoteAsync(string symbol, CancellationToken ct)
    {
        var s = Normalize(symbol);
        if (s.Length == 0) return null;

        var url = $"{ChartBase}/{Uri.EscapeDataString(s)}?interval=1d&range=1d";
        var resp = await api.GetJsonAsync<ChartResponse>(url, ct);
        var meta = resp?.Chart?.Result?.FirstOrDefault()?.Meta;
        if (meta?.RegularMarketPrice is not { } price)
        {
            log.LogDebug("No quote for {Symbol}", s);
            return null;
        }

        var name = FirstNonBlank(meta.ShortName, meta.LongName) ?? s;
        var prevClose = meta.ChartPreviousClose ?? meta.PreviousClose ?? price;
        var time = meta.RegularMarketTime is { } t ? DateTimeOffset.FromUnixTimeSeconds(t) : (DateTimeOffset?)null;
        return new TickerQuote(s, name, meta.Currency ?? "", price, prevClose, time);
    }

    /// <summary>Symbol-search suggestions for autocomplete (equities, ETFs, indices, …).</summary>
    public async Task<IReadOnlyList<TickerSearchResultDto>> SearchAsync(string query, CancellationToken ct)
    {
        var q = query.Trim();
        if (q.Length == 0) return Array.Empty<TickerSearchResultDto>();

        var url = $"{SearchBase}?q={Uri.EscapeDataString(q)}&quotesCount=8&newsCount=0";
        var resp = await api.GetJsonAsync<SearchResponse>(url, ct);
        if (resp?.Quotes is null) return Array.Empty<TickerSearchResultDto>();

        return resp.Quotes
            .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
            .Select(x => new TickerSearchResultDto(
                Normalize(x.Symbol!),
                FirstNonBlank(x.Shortname, x.Longname) ?? x.Symbol!,
                x.ExchDisp ?? "",
                x.QuoteType ?? ""))
            .ToList();
    }

    /// <summary>Copies a fresh quote onto a stored ticker row (shared by the add endpoint and the worker).</summary>
    public static void Apply(Ticker ticker, TickerQuote q)
    {
        ticker.Name = q.Name;
        ticker.Currency = q.Currency;
        ticker.Price = q.Price;
        ticker.PreviousClose = q.PreviousClose;
        ticker.MarketTime = q.MarketTime;
        ticker.UpdatedAt = DateTimeOffset.UtcNow;
        ticker.LastError = null;
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    // ── Yahoo response shapes (camelCase fields; ExternalApiClient deserializes case-insensitively) ──
    private sealed record ChartResponse(ChartBlock? Chart);
    private sealed record ChartBlock(List<ChartResult>? Result);
    private sealed record ChartResult(ChartMeta? Meta);
    private sealed record ChartMeta(
        string? Symbol, string? Currency, string? ShortName, string? LongName,
        double? RegularMarketPrice, double? ChartPreviousClose, double? PreviousClose, long? RegularMarketTime);

    private sealed record SearchResponse(List<SearchQuote>? Quotes);
    private sealed record SearchQuote(string? Symbol, string? Shortname, string? Longname, string? ExchDisp, string? QuoteType);
}

/// <summary>A point-in-time quote for a symbol.</summary>
public sealed record TickerQuote(string Symbol, string Name, string Currency, double Price, double PreviousClose, DateTimeOffset? MarketTime);
