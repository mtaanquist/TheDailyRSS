using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using TheDailyRSS.Client;
using TheDailyRSS.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// API client: every request carries the bearer token via AuthMessageHandler.
// TokenStore MUST be a singleton: IHttpClientFactory resolves the handler in its own
// scope, so a scoped store would be a different instance than AuthService writes to.
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddScoped<AuthMessageHandler>();
builder.Services.AddHttpClient("api", client =>
        client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<AuthMessageHandler>();
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("api"));

builder.Services.AddScoped<LocalStorage>();
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<AppState>();

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthStateProvider>();
builder.Services.AddCascadingAuthenticationState();

var host = builder.Build();

// Restore session + apply theme before the first render.
await host.Services.GetRequiredService<AuthService>().InitializeAsync();
await host.Services.GetRequiredService<ThemeService>().InitializeAsync();

// Resolve the engine once so NewsImage can pick eager vs lazy loading at first render
// (WebKit's native lazy-loading misbehaves inside our non-document scroller — see BrowserEnv).
BrowserEnv.IsWebKit = await host.Services.GetRequiredService<IJSRuntime>().InvokeAsync<bool>("tdrEnv.isWebKit");

await host.RunAsync();
