using Microsoft.JSInterop;

namespace TheDailyRSS.Client.Services;

/// <summary>Thin wrapper over the browser's localStorage.</summary>
public sealed class LocalStorage(IJSRuntime js)
{
    public async ValueTask<string?> GetAsync(string key) =>
        await js.InvokeAsync<string?>("localStorage.getItem", key);

    public async ValueTask SetAsync(string key, string value) =>
        await js.InvokeVoidAsync("localStorage.setItem", key, value);

    public async ValueTask RemoveAsync(string key) =>
        await js.InvokeVoidAsync("localStorage.removeItem", key);
}
