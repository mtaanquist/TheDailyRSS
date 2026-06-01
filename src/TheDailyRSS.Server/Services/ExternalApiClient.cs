using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TheDailyRSS.Server.Services;

/// <summary>
/// Typed GET-JSON helper for the app's own integrations with <b>fixed, trusted</b> third-party APIs
/// (weather, stock quotes, …) — as opposed to the user-supplied URLs handled by the "feeds"/"ai"
/// clients. Shared by the data features so each one doesn't re-implement the same timeout, response-size
/// cap, deserialization and (importantly) graceful-failure handling.
///
/// <para>Like <see cref="ArticleContentExtractor"/>, it never throws for an expected failure: a non-success
/// status, an oversized body, a parse error or a network blip all return <c>default</c>, so a polling
/// background worker treats "couldn't fetch this tick" as a no-op rather than crashing its sweep. Caller
/// cancellation still propagates. Stateless, so registered as a singleton.</para>
/// </summary>
public sealed class ExternalApiClient(
    IHttpClientFactory httpFactory,
    IOptions<FeedOptions> options,
    ILogger<ExternalApiClient> log)
{
    private readonly FeedOptions _options = options.Value;

    // Web defaults: camelCase + case-insensitive, matching how most public JSON APIs shape their fields.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>GETs <paramref name="url"/> and deserializes the JSON body to <typeparamref name="T"/>, or
    /// returns <c>null</c> when the call fails, isn't successful, or the body is unparseable/too large.</summary>
    public async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct) where T : class
    {
        try
        {
            var http = httpFactory.CreateClient("external");
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogDebug("External API GET {Url} returned {Status}", url, (int)resp.StatusCode);
                return null;
            }

            // Cap the body even though the named client sets MaxResponseContentBufferSize — that limit
            // doesn't apply to ReadAsStreamAsync, so the bound is enforced here for the streamed read.
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var buffered = await HttpStreamUtil.ReadCappedAsync(stream, _options.MaxResponseBytes, ct);
            buffered.Position = 0;

            return await JsonSerializer.DeserializeAsync<T>(buffered, JsonOptions, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "External API GET {Url} failed", url);
            return null;
        }
    }
}
