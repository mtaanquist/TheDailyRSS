using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Services;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

public static class TickerEndpoints
{
    public static void MapTickerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tickers").RequireAuthorization();
        group.MapGet("", List);
        group.MapGet("/search", Search);
        group.MapPost("", Add);
        group.MapPut("/{symbol}", Update);
        group.MapDelete("/{symbol}", Remove);
    }

    /// <summary>The reader's tracked tickers with their latest quotes, in display order.</summary>
    private static async Task<IResult> List(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        var rows = await db.UserTickers
            .Where(x => x.UserId == uid)
            .Include(x => x.Ticker)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Symbol)
            .ToListAsync(ct);
        return Results.Ok(rows.Select(r => r.ToDto()).ToList());
    }

    private static async Task<IResult> Search(string? q, TickerService tickers, CancellationToken ct) =>
        Results.Ok(await tickers.SearchAsync(q ?? "", ct));

    /// <summary>Tracks a new symbol. Validates it by fetching a quote, then ensures the shared ticker row.</summary>
    private static async Task<IResult> Add(
        AddTickerRequest req, ClaimsPrincipal principal, AppDbContext db, TickerService tickers, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        var symbol = TickerService.Normalize(req.Symbol ?? "");
        if (symbol.Length == 0) return ApiResults.Fail("Enter a ticker symbol.");

        if (await db.UserTickers.AnyAsync(x => x.UserId == uid && x.Symbol == symbol, ct))
            return ApiResults.Conflict($"You're already tracking {symbol}.");

        var quote = await tickers.FetchQuoteAsync(symbol, ct);
        if (quote is null) return ApiResults.Fail($"Couldn't find a ticker called “{symbol}”.");

        var ticker = await db.Tickers.FirstOrDefaultAsync(t => t.Symbol == symbol, ct);
        if (ticker is null)
        {
            ticker = new Ticker { Symbol = symbol };
            db.Tickers.Add(ticker);
        }
        TickerService.Apply(ticker, quote);

        var nextOrder = (await db.UserTickers.Where(x => x.UserId == uid).Select(x => (int?)x.SortOrder).MaxAsync(ct) ?? -1) + 1;
        var sub = new UserTicker { UserId = uid, Symbol = symbol, SortOrder = nextOrder };
        db.UserTickers.Add(sub);
        await db.SaveChangesAsync(ct);

        sub.Ticker = ticker;
        return Results.Ok(sub.ToDto());
    }

    /// <summary>Promotes/demotes a tracked ticker for the front-page bar.</summary>
    private static async Task<IResult> Update(
        string symbol, UpdateTickerRequest req, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        var sym = TickerService.Normalize(symbol);
        var sub = await db.UserTickers.Include(x => x.Ticker)
            .FirstOrDefaultAsync(x => x.UserId == uid && x.Symbol == sym, ct);
        if (sub is null) return Results.NotFound();

        sub.Promoted = req.Promoted;
        await db.SaveChangesAsync(ct);
        return Results.Ok(sub.ToDto());
    }

    private static async Task<IResult> Remove(
        string symbol, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        var sym = TickerService.Normalize(symbol);
        await db.UserTickers.Where(x => x.UserId == uid && x.Symbol == sym).ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }
}
