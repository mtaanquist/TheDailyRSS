using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Services;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

public static class WeatherEndpoints
{
    public static void MapWeatherEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/weather").RequireAuthorization();
        group.MapGet("/{date}", GetForDate);
        group.MapPut("/location", SetLocation);
    }

    /// <summary>Geocodes and saves the reader's weather location (empty query clears it). Returns the
    /// updated user so the client picks up the new <c>WeatherLocation</c> label.</summary>
    private static async Task<IResult> SetLocation(
        SetWeatherLocationRequest req, ClaimsPrincipal principal,
        UserManager<AppUser> users, WeatherService weather, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(principal.GetUserId().ToString());
        if (user is null) return Results.Unauthorized();

        var query = (req.Query ?? "").Trim();
        if (query.Length == 0)
        {
            user.WeatherLocationName = null;
            user.WeatherLatitude = null;
            user.WeatherLongitude = null;
        }
        else
        {
            var geo = await weather.GeocodeAsync(query, ct);
            if (geo is null) return ApiResults.Fail($"Couldn't find a place called “{query}”.");
            user.WeatherLocationName = geo.Name;
            user.WeatherLatitude = geo.Latitude;
            user.WeatherLongitude = geo.Longitude;
        }

        await users.UpdateAsync(user);
        return Results.Ok(user.ToDto(await users.GetRolesAsync(user)));
    }

    /// <summary>The stored forecast for the reader's location on a given edition day. 204 when no location
    /// is set or nothing is on file. For today, lazily fetches and stores so weather appears right after a
    /// reader sets their location, without waiting for the next background sweep.</summary>
    private static async Task<IResult> GetForDate(
        string date, ClaimsPrincipal principal, AppDbContext db,
        IOptions<FeedOptions> opts, WeatherService weather, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var d)) return ApiResults.Fail("Invalid date.");

        var uid = principal.GetUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid, ct);
        if (user?.WeatherLatitude is not { } lat || user.WeatherLongitude is not { } lon)
            return Results.NoContent();

        var key = WeatherService.LocationKey(lat, lon);
        var snap = await db.WeatherSnapshots
            .FirstOrDefaultAsync(s => s.LocationKey == key && s.EditionDate == d, ct);

        if (snap is null && d == EditionClock.Today(opts.Value))
        {
            snap = await weather.FetchAsync(lat, lon, d, ct);
            if (snap is not null)
            {
                db.WeatherSnapshots.Add(snap);
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // The worker (or a concurrent request) inserted it first — read theirs back.
                    db.ChangeTracker.Clear();
                    snap = await db.WeatherSnapshots
                        .FirstOrDefaultAsync(s => s.LocationKey == key && s.EditionDate == d, ct);
                }
            }
        }

        return snap is null
            ? Results.NoContent()
            : Results.Ok(snap.ToWeatherDto(user.WeatherLocationName ?? ""));
    }
}
