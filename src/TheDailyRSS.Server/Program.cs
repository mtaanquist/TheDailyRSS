using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Endpoints;
using TheDailyRSS.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Data directory (key material, etc.) ─────────────────────────────
var dataDir = builder.Configuration["DataDir"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data");

// ── Options ─────────────────────────────────────────────────────────
builder.Services.Configure<FeedOptions>(builder.Configuration.GetSection(FeedOptions.SectionName));

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
builder.Services.Configure<JwtOptions>(jwtSection);
var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.Key))
{
    jwtOptions.Key = JwtKeyBootstrap.LoadOrCreate(dataDir);
    builder.Services.PostConfigure<JwtOptions>(o => o.Key = jwtOptions.Key);
}

// ── Database ────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=thedailyrss;Username=postgres;Password=postgres";
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

// ── Identity ────────────────────────────────────────────────────────
builder.Services
    .AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>();

// ── Authentication / Authorization ──────────────────────────────────
builder.Services.AddSingleton<JwtTokenService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
        // Reject tokens whose session has been revoked; keep "last seen" fresh.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var sid = ctx.Principal!.GetSessionId();
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sid);
                if (session is null || session.RevokedAt is not null)
                {
                    ctx.Fail("Session revoked.");
                    return;
                }
                if (DateTimeOffset.UtcNow - session.LastSeenAt > TimeSpan.FromMinutes(5))
                {
                    session.LastSeenAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();
                }
            },
        };
    });
builder.Services.AddAuthorization(o =>
    o.AddPolicy(Roles.Admin, p => p.RequireRole(Roles.Admin)));

// ── App services ────────────────────────────────────────────────────
// Both outbound clients fetch user-supplied URLs, so both use the SSRF-guarded handler that
// blocks private/loopback/link-local targets, and both cap the buffered response body.
var maxResponseBytes = builder.Configuration.GetSection(FeedOptions.SectionName)
    .Get<FeedOptions>()?.MaxResponseBytes ?? new FeedOptions().MaxResponseBytes;

builder.Services.AddHttpClient("feeds", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.MaxResponseContentBufferSize = maxResponseBytes;
    c.DefaultRequestHeaders.UserAgent.ParseAdd("TheDailyRSS/1.0 (+https://github.com/self-hosted)");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml, text/xml, text/html;q=0.8");
}).ConfigurePrimaryHttpMessageHandler(() => SsrfGuard.CreateHandler(allowAutoRedirect: true, maxRedirects: 5));

// Client for users' own OpenAI-compatible LLM endpoints (BYOK summaries).
builder.Services.AddHttpClient("ai", c =>
{
    c.Timeout = TimeSpan.FromSeconds(120);
    c.MaxResponseContentBufferSize = maxResponseBytes;
}).ConfigurePrimaryHttpMessageHandler(() => SsrfGuard.CreateHandler(allowAutoRedirect: false));

// Persist DataProtection keys to the data volume so they survive container restarts.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

builder.Services.AddSingleton<FeedReader>();
builder.Services.AddSingleton<HtmlSanitizationService>();
builder.Services.AddScoped<FeedDiscoveryService>();
builder.Services.AddScoped<FeedFetchService>();
builder.Services.AddScoped<FeedSourceService>();
builder.Services.AddScoped<OpmlService>();
builder.Services.AddScoped<AiSummaryService>();
builder.Services.AddHostedService<FeedRefreshBackgroundService>();
builder.Services.AddHostedService<AiSummaryBackgroundService>();

var app = builder.Build();

// ── Migrate on startup ──────────────────────────────────────────────
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    // Ensure the Admin role exists so the first registrant can be promoted into it.
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    if (!await roles.RoleExistsAsync(Roles.Admin))
        await roles.CreateAsync(new IdentityRole<Guid>(Roles.Admin));
}

if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();

// ── Security headers ────────────────────────────────────────────────
// Defence-in-depth behind the server-side HTML sanitizer: even if some markup slips through,
// the CSP forbids inline/external script execution and blocks framing/clickjacking.
// Blazor WASM needs 'wasm-unsafe-eval' for the .NET runtime; the app's own JS lives in external
// files (js/app.js) so script-src stays strict (no 'unsafe-inline'). Google Fonts is allowlisted
// for styles/fonts; feed images come from arbitrary http(s) hosts (and data: URIs), so img-src is broad.
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'wasm-unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "img-src 'self' data: https: http:; " +
        "font-src 'self' data: https://fonts.gstatic.com; " +
        "connect-src 'self'; " +
        "frame-src 'none'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapCategoryEndpoints();
app.MapFeedEndpoints();
app.MapEditionEndpoints();
app.MapKeywordEndpoints();
app.MapFieldFilterEndpoints();
app.MapAdminEndpoints();
app.MapAiEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed so integration tests can host the app via WebApplicationFactory.</summary>
public partial class Program;
