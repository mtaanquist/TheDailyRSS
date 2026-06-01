using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Services;
using Xunit;

namespace TheDailyRSS.Tests;

public class TickerServiceTests
{
    private const string ChartJson = """
        { "chart": { "result": [ { "meta": {
            "currency": "USD", "symbol": "AAPL", "shortName": "Apple Inc.",
            "regularMarketPrice": 201.5, "chartPreviousClose": 198.0, "regularMarketTime": 1717000000
        } } ], "error": null } }
        """;
    private const string SearchJson = """
        { "quotes": [
            { "symbol": "aapl", "shortname": "Apple Inc.", "exchDisp": "NASDAQ", "quoteType": "EQUITY" },
            { "shortname": "no symbol here" }
        ] }
        """;

    private static TickerService Build(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var client = new HttpClient(new StubHandler(responder));
        var api = new ExternalApiClient(new StubFactory(client), Options.Create(new FeedOptions()), NullLogger<ExternalApiClient>.Instance);
        return new TickerService(api, NullLogger<TickerService>.Instance);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(" aapl ", "AAPL")]
    [InlineData("^gspc", "^GSPC")]
    public void Normalize_trims_and_uppercases(string input, string expected) =>
        Assert.Equal(expected, TickerService.Normalize(input));

    [Fact]
    public async Task FetchQuote_maps_price_name_and_previous_close()
    {
        var sut = Build(_ => Json(ChartJson));
        var q = await sut.FetchQuoteAsync("aapl", default);

        Assert.NotNull(q);
        Assert.Equal("AAPL", q!.Symbol);
        Assert.Equal("Apple Inc.", q.Name);
        Assert.Equal("USD", q.Currency);
        Assert.Equal(201.5, q.Price, 3);
        Assert.Equal(198.0, q.PreviousClose, 3);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1717000000), q.MarketTime);
    }

    [Fact]
    public async Task FetchQuote_returns_null_when_no_result()
    {
        var sut = Build(_ => Json("""{ "chart": { "result": [], "error": null } }"""));
        Assert.Null(await sut.FetchQuoteAsync("ZZZZ", default));
    }

    [Fact]
    public async Task FetchQuote_returns_null_when_the_api_fails()
    {
        var sut = Build(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        Assert.Null(await sut.FetchQuoteAsync("AAPL", default));
    }

    [Fact]
    public async Task Search_maps_and_normalizes_and_drops_blank_symbols()
    {
        var sut = Build(_ => Json(SearchJson));
        var results = await sut.SearchAsync("apple", default);

        var hit = Assert.Single(results);
        Assert.Equal("AAPL", hit.Symbol);
        Assert.Equal("Apple Inc.", hit.Name);
        Assert.Equal("NASDAQ", hit.Exchange);
        Assert.Equal("EQUITY", hit.Type);
    }

    [Fact]
    public async Task Search_returns_empty_for_blank_query_without_calling_out()
    {
        var called = false;
        var sut = Build(_ => { called = true; return Json(SearchJson); });
        Assert.Empty(await sut.SearchAsync("  ", default));
        Assert.False(called);
    }

    [Fact]
    public void Apply_copies_quote_onto_row_and_clears_error()
    {
        var row = new Ticker { Symbol = "AAPL", LastError = "stale" };
        TickerService.Apply(row, new TickerQuote("AAPL", "Apple Inc.", "USD", 201.5, 198.0, DateTimeOffset.UnixEpoch));

        Assert.Equal("Apple Inc.", row.Name);
        Assert.Equal(201.5, row.Price, 3);
        Assert.Equal(198.0, row.PreviousClose, 3);
        Assert.Null(row.LastError);
        Assert.NotNull(row.UpdatedAt);
    }

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responder(request));
        }
    }
}
