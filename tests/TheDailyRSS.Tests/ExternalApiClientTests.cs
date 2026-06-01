using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheDailyRSS.Server.Services;
using Xunit;

namespace TheDailyRSS.Tests;

public class ExternalApiClientTests
{
    private sealed record Quote(string Symbol, decimal Price);

    private static ExternalApiClient Build(Func<HttpRequestMessage, HttpResponseMessage> responder, int maxBytes = 1_000_000)
    {
        var client = new HttpClient(new StubHandler(responder));
        var factory = new StubFactory(client);
        var options = Options.Create(new FeedOptions { MaxResponseBytes = maxBytes });
        return new ExternalApiClient(factory, options, NullLogger<ExternalApiClient>.Instance);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Deserializes_a_successful_json_response()
    {
        // Web defaults → camelCase + case-insensitive, like a typical public API.
        var sut = Build(_ => Json("""{"symbol":"ACME","price":42.5}"""));
        var quote = await sut.GetJsonAsync<Quote>("https://api.test/quote/ACME", default);
        Assert.NotNull(quote);
        Assert.Equal("ACME", quote!.Symbol);
        Assert.Equal(42.5m, quote.Price);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Returns_null_on_non_success_status(HttpStatusCode status)
    {
        var sut = Build(_ => Json("""{"symbol":"X","price":1}""", status));
        Assert.Null(await sut.GetJsonAsync<Quote>("https://api.test/x", default));
    }

    [Fact]
    public async Task Returns_null_when_the_transport_throws()
    {
        var sut = Build(_ => throw new HttpRequestException("boom"));
        Assert.Null(await sut.GetJsonAsync<Quote>("https://api.test/x", default));
    }

    [Fact]
    public async Task Returns_null_on_unparseable_body()
    {
        var sut = Build(_ => Json("not json at all"));
        Assert.Null(await sut.GetJsonAsync<Quote>("https://api.test/x", default));
    }

    [Fact]
    public async Task Returns_null_when_the_body_exceeds_the_cap()
    {
        var big = "{\"symbol\":\"" + new string('A', 5_000) + "\",\"price\":1}";
        var sut = Build(_ => Json(big), maxBytes: 512);
        Assert.Null(await sut.GetJsonAsync<Quote>("https://api.test/x", default));
    }

    [Fact]
    public async Task Propagates_caller_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = Build(_ => Json("{}"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.GetJsonAsync<Quote>("https://api.test/x", cts.Token));
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
