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
builder.Services.AddAuthorization();

// ── App services ────────────────────────────────────────────────────
builder.Services.AddHttpClient("feeds", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("TheDailyRSS/1.0 (+https://github.com/self-hosted)");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml, text/xml, text/html;q=0.8");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 });

// Persist DataProtection keys to the data volume so they survive container restarts.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

builder.Services.AddSingleton<FeedReader>();
builder.Services.AddScoped<FeedDiscoveryService>();
builder.Services.AddScoped<FeedFetchService>();
builder.Services.AddScoped<OpmlService>();
builder.Services.AddHostedService<FeedRefreshBackgroundService>();

var app = builder.Build();

// ── Migrate on startup ──────────────────────────────────────────────
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapCategoryEndpoints();
app.MapFeedEndpoints();
app.MapEditionEndpoints();

app.MapFallbackToFile("index.html");

app.Run();
