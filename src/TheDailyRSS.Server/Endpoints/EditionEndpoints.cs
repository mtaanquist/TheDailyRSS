using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Services;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

public static class EditionEndpoints
{
    public static void MapEditionEndpoints(this IEndpointRouteBuilder app)
    {
        var editions = app.MapGroup("/api/editions").RequireAuthorization();
        editions.MapGet("/dates", ListDates);
        editions.MapGet("/latest", Latest);
        editions.MapGet("/{date}", GetEdition);
        editions.MapPost("/{date}/mark-read", MarkEditionRead);

        var articles = app.MapGroup("/api/articles").RequireAuthorization();
        articles.MapGet("/{id:guid}", GetArticle);
        articles.MapPost("/{id:guid}/read", SetRead);
        articles.MapPost("/{id:guid}/save", SetSaved);
        articles.MapPost("/{id:guid}/position", SetPosition);
    }

    private static async Task<IResult> ListDates(ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var grouped = await db.Articles
            .Where(a => a.Feed!.UserId == uid)
            .GroupBy(a => a.EditionDate)
            .Select(g => new { Date = g.Key, Count = g.Count(), Unread = g.Sum(a => a.IsRead ? 0 : 1) })
            .OrderByDescending(x => x.Date)
            .ToListAsync();
        return Results.Ok(grouped.Select(x => new EditionDateDto(x.Date, x.Count, x.Unread)));
    }

    private static async Task<IResult> Latest(
        ClaimsPrincipal principal, AppDbContext db, IOptions<FeedOptions> opts,
        Guid? categoryId, bool? unreadOnly)
    {
        var uid = principal.GetUserId();
        var latest = await db.Articles
            .Where(a => a.Feed!.UserId == uid)
            .MaxAsync(a => (DateOnly?)a.EditionDate);
        var date = latest ?? Today(opts.Value);
        return await BuildEdition(uid, date, categoryId, saved: false, unreadOnly ?? false, db, opts.Value);
    }

    private static async Task<IResult> GetEdition(
        string date, ClaimsPrincipal principal, AppDbContext db, IOptions<FeedOptions> opts,
        Guid? categoryId, bool? saved, bool? unreadOnly)
    {
        if (!DateOnly.TryParse(date, out var d))
            return Results.BadRequest(new { error = "Invalid date." });
        return await BuildEdition(principal.GetUserId(), d, categoryId, saved ?? false, unreadOnly ?? false, db, opts.Value);
    }

    private static async Task<IResult> BuildEdition(
        Guid uid, DateOnly date, Guid? categoryId, bool saved, bool unreadOnly,
        AppDbContext db, FeedOptions opts)
    {
        var query = db.Articles.Where(a => a.Feed!.UserId == uid);

        // "Saved" is a cross-date pseudo-section; everything else is bound to the day.
        if (saved) query = query.Where(a => a.IsSaved);
        else query = query.Where(a => a.EditionDate == date);

        if (categoryId is { } cid) query = query.Where(a => a.Feed!.CategoryId == cid);
        if (unreadOnly) query = query.Where(a => !a.IsRead);

        var rows = await query
            .Include(a => a.Feed!).ThenInclude(f => f.Category!)
            .OrderByDescending(a => a.PublishedAt)
            .Take(300)
            .ToListAsync();

        var summaries = rows.Select(r => r.ToSummary()).ToList();

        // Lead: newest article that has an image, otherwise just the newest.
        var lead = summaries.FirstOrDefault(s => s.ImageUrl is not null) ?? summaries.FirstOrDefault();
        var rest = summaries.Where(s => lead is null || s.Id != lead.Id).ToList();

        var sections = summaries
            .GroupBy(s => (s.CategoryId, s.CategoryName, s.CategoryColor))
            .Select(g => new EditionSectionDto(
                g.Key.CategoryId, g.Key.CategoryName, g.Key.CategoryColor, g.Count(), g.ToList()))
            .OrderByDescending(s => s.Count)
            .ToList();

        var unreadTotal = await db.Articles.CountAsync(a => a.Feed!.UserId == uid && !a.IsRead);

        DateOnly? prev = null, next = null;
        if (!saved)
        {
            var dateQuery = db.Articles.Where(a => a.Feed!.UserId == uid);
            if (categoryId is { } c2) dateQuery = dateQuery.Where(a => a.Feed!.CategoryId == c2);
            prev = await dateQuery.Where(a => a.EditionDate < date).MaxAsync(a => (DateOnly?)a.EditionDate);
            next = await dateQuery.Where(a => a.EditionDate > date).MinAsync(a => (DateOnly?)a.EditionDate);
        }

        var categoryName = categoryId is null ? null
            : rows.FirstOrDefault()?.Feed!.Category!.Name
              ?? (await db.Categories.Where(c => c.Id == categoryId).Select(c => c.Name).FirstOrDefaultAsync());

        var dto = new EditionDto(
            Date: date,
            VolumeLabel: Masthead.Volume(date),
            IssueLabel: Masthead.Issue(date),
            DateLabel: Masthead.DateLabel(date),
            IsToday: date == Today(opts),
            PrevDate: prev,
            NextDate: next,
            UnreadTotal: unreadTotal,
            CategoryId: categoryId,
            CategoryName: saved ? "Saved" : categoryName,
            Lead: lead,
            Articles: rest,
            Sections: sections);

        return Results.Ok(dto);
    }

    private static async Task<IResult> GetArticle(Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var article = await db.Articles
            .Include(a => a.Feed!).ThenInclude(f => f.Category!)
            .FirstOrDefaultAsync(a => a.Id == id && a.Feed!.UserId == uid);
        return article is null ? Results.NotFound() : Results.Ok(article.ToDto());
    }

    private static async Task<IResult> SetRead(Guid id, SetBoolRequest req, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var n = await db.Articles.Where(a => a.Id == id && a.Feed!.UserId == uid)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsRead, req.Value));
        return n > 0 ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> SetSaved(Guid id, SetBoolRequest req, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var n = await db.Articles.Where(a => a.Id == id && a.Feed!.UserId == uid)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsSaved, req.Value));
        return n > 0 ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> SetPosition(Guid id, SetPositionRequest req, ClaimsPrincipal principal, AppDbContext db)
    {
        var uid = principal.GetUserId();
        var pct = Math.Clamp(req.Percent, 0, 100);
        var n = await db.Articles.Where(a => a.Id == id && a.Feed!.UserId == uid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.ReadingPositionPercent, pct)
                .SetProperty(a => a.IsRead, a => a.IsRead || pct >= 90));
        return n > 0 ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> MarkEditionRead(
        string date, ClaimsPrincipal principal, AppDbContext db, Guid? categoryId)
    {
        if (!DateOnly.TryParse(date, out var d))
            return Results.BadRequest(new { error = "Invalid date." });

        var uid = principal.GetUserId();
        var query = db.Articles.Where(a => a.Feed!.UserId == uid && a.EditionDate == d && !a.IsRead);
        if (categoryId is { } cid) query = query.Where(a => a.Feed!.CategoryId == cid);
        var n = await query.ExecuteUpdateAsync(s => s.SetProperty(a => a.IsRead, true));
        return Results.Ok(new { marked = n });
    }

    private static DateOnly Today(FeedOptions opts)
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(opts.EditionTimeZone); }
        catch { tz = TimeZoneInfo.Utc; }
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
    }
}

public sealed record SetBoolRequest(bool Value);
public sealed record SetPositionRequest(int Percent);

/// <summary>Charming, deterministic newspaper masthead strings derived from the date.</summary>
public static class Masthead
{
    private static readonly DateOnly Epoch = new(2024, 1, 1);

    public static string Issue(DateOnly date)
    {
        var n = Math.Max(1, date.DayNumber - Epoch.DayNumber + 1);
        return $"NO. {n}";
    }

    public static string Volume(DateOnly date) => "VOL. " + ToRoman(date.Year - Epoch.Year + 1);

    public static string DateLabel(DateOnly date) =>
        date.ToString("dddd · MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant();

    private static string ToRoman(int n)
    {
        if (n <= 0) return "I";
        var map = new (int v, string s)[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") };
        var sb = new System.Text.StringBuilder();
        foreach (var (v, s) in map)
            while (n >= v) { sb.Append(s); n -= v; }
        return sb.ToString();
    }
}
