using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Services;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

/// <summary>Instance-wide configuration the signed-in client loads once at boot to gate features
/// (e.g. hiding the share affordance when an admin has turned sharing off).</summary>
public static class InstanceEndpoints
{
    public static void MapInstanceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/instance", GetConfig).RequireAuthorization();
    }

    private static async Task<IResult> GetConfig(AppDbContext db, CancellationToken ct)
    {
        var sharingEnabled = !await SiteSettings.IsSharingDisabledAsync(db, ct);
        return Results.Ok(new InstanceConfigDto(sharingEnabled));
    }
}
