using System.Net.Http.Headers;

namespace TheDailyRSS.Client.Services;

/// <summary>Attaches the bearer token to every API request.</summary>
public sealed class AuthMessageHandler(TokenStore store) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(store.Token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", store.Token);
        return base.SendAsync(request, ct);
    }
}
