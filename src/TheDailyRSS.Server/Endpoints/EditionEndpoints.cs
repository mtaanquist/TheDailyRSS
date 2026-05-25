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
    /// <summary>How many articles each category contributes to the curated front page.</summary>
    private const int FrontPageSectionSize = 5;

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

    // ── Visibility & keyword helpers ────────────────────────────────────

    /// <summary>Articles the user can see: those belonging to a source they subscribe to.</summary>
    private static IQueryable<Article> Subscribed(AppDbContext db, Guid uid) =>
        db.Articles.Where(a => a.Source!.Subscriptions.Any(s => s.UserId == uid));

    /// <summary>Drops articles matching any of the user's mute terms (case-insensitive).</summary>
    private static IQueryable<Article> ApplyKeywords(IQueryable<Article> q, List<KeywordFilter> filters)
    {
        foreach (var f in filters)
        {
            var like = $"%{f.Term}%";
            if (f.Scope == KeywordScope.TitleOnly)
                q = q.Where(a => !EF.Functions.ILike(a.Title, like));
            else
                q = q.Where(a => !EF.Functions.ILike(a.Title, like)
                    && !(a.Summary != null && EF.Functions.ILike(a.Summary, like)));
        }
        return q;
    }

    private static async Task<List<KeywordFilter>> LoadFiltersAsync(AppDbContext db, Guid uid, CancellationToken ct) =>
        await db.KeywordFilters.Where(k => k.UserId == uid).ToListAsync(ct);

    /// <summary>Projects subscribed articles to summaries, resolving the per-user category + state.</summary>
    private static IQueryable<ArticleSummaryDto> ToSummaries(IQueryable<Article> articles, AppDbContext db, Guid uid) =>
        from a in articles
        from sub in db.Subscriptions.Where(s => s.UserId == uid && s.SourceId == a.SourceId)
        from st in db.UserArticleStates.Where(s => s.UserId == uid && s.ArticleId == a.Id).DefaultIfEmpty()
        select new ArticleSummaryDto(
            a.Id, a.Title, a.Summary,
            sub.CustomTitle ?? a.Source!.Title, a.Source!.IconText,
            sub.CategoryId, sub.Category!.Name, sub.Category.Color,
            a.ImageUrl, a.PublishedAt,
            st != null && st.IsRead, st != null && st.IsSaved, a.Url);

    // ── Reads ───────────────────────────────────────────────────────────

    private static async Task<IResult> ListDates(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        var filters = await LoadFiltersAsync(db, uid, ct);
        var visible = ApplyKeywords(Subscribed(db, uid), filters);

        var grouped = await (
            from a in visible
            from st in db.UserArticleStates.Where(s => s.UserId == uid && s.ArticleId == a.Id).DefaultIfEmpty()
            group new { st } by a.EditionDate into g
            select new
            {
                Date = g.Key,
                Count = g.Count(),
                Unread = g.Count(x => x.st == null || !x.st.IsRead),
            })
            .OrderByDescending(x => x.Date)
            .ToListAsync(ct);

        return Results.Ok(grouped.Select(x => new EditionDateDto(x.Date, x.Count, x.Unread)));
    }

    private static async Task<IResult> Latest(
        ClaimsPrincipal principal, AppDbContext db, IOptions<FeedOptions> opts,
        Guid? categoryId, Guid? sourceId, bool? unreadOnly, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        var latestQuery = Subscribed(db, uid);
        if (sourceId is { } sid) latestQuery = latestQuery.Where(a => a.SourceId == sid);
        var latest = await latestQuery.MaxAsync(a => (DateOnly?)a.EditionDate, ct);
        var date = latest ?? Today(opts.Value);
        return await BuildEdition(uid, date, categoryId, sourceId, saved: false, unreadOnly ?? false, db, opts.Value, ct);
    }

    private static async Task<IResult> GetEdition(
        string date, ClaimsPrincipal principal, AppDbContext db, IOptions<FeedOptions> opts,
        Guid? categoryId, Guid? sourceId, bool? saved, bool? unreadOnly, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var d))
            return Results.BadRequest(new { error = "Invalid date." });
        return await BuildEdition(principal.GetUserId(), d, categoryId, sourceId, saved ?? false, unreadOnly ?? false, db, opts.Value, ct);
    }

    private static async Task<IResult> BuildEdition(
        Guid uid, DateOnly date, Guid? categoryId, Guid? sourceId, bool saved, bool unreadOnly,
        AppDbContext db, FeedOptions opts, CancellationToken ct)
    {
        var filters = await LoadFiltersAsync(db, uid, ct);
        var query = ApplyKeywords(Subscribed(db, uid), filters);

        // "Saved" is a cross-date pseudo-section; everything else is bound to the day.
        if (saved) query = query.Where(a => a.States.Any(s => s.UserId == uid && s.IsSaved));
        else query = query.Where(a => a.EditionDate == date);

        if (categoryId is { } cid)
            query = query.Where(a => a.Source!.Subscriptions.Any(s => s.UserId == uid && s.CategoryId == cid));
        if (sourceId is { } src)
            query = query.Where(a => a.SourceId == src);
        if (unreadOnly)
            query = query.Where(a => !a.States.Any(s => s.UserId == uid && s.IsRead));

        var top = query.OrderByDescending(a => a.PublishedAt).Take(300);
        var summaries = (await ToSummaries(top, db, uid).ToListAsync(ct))
            .OrderByDescending(s => s.PublishedAt)
            .ToList();

        // Lead: newest article that has an image, otherwise just the newest.
        var lead = summaries.FirstOrDefault(s => s.ImageUrl is not null) ?? summaries.FirstOrDefault();
        var rest = summaries.Where(s => lead is null || s.Id != lead.Id).ToList();

        // Front page (no drill-down): a curated slice of every category, in taxonomy order.
        // A single-category view keeps the full flat list.
        List<EditionSectionDto> sections;
        if (categoryId is null && sourceId is null && !saved)
        {
            var order = await db.Categories.ToDictionaryAsync(c => c.Id, c => c.SortOrder, ct);
            sections = rest
                .GroupBy(s => (s.CategoryId, s.CategoryName, s.CategoryColor))
                .Select(g => new EditionSectionDto(
                    g.Key.CategoryId, g.Key.CategoryName, g.Key.CategoryColor,
                    g.Count(), g.Take(FrontPageSectionSize).ToList()))
                .OrderBy(s => order.TryGetValue(s.CategoryId, out var o) ? o : int.MaxValue)
                .ToList();
        }
        else
        {
            sections = summaries
                .GroupBy(s => (s.CategoryId, s.CategoryName, s.CategoryColor))
                .Select(g => new EditionSectionDto(
                    g.Key.CategoryId, g.Key.CategoryName, g.Key.CategoryColor, g.Count(), g.ToList()))
                .ToList();
        }

        var unreadTotal = await ApplyKeywords(Subscribed(db, uid), filters)
            .CountAsync(a => !a.States.Any(s => s.UserId == uid && s.IsRead), ct);

        DateOnly? prev = null, next = null;
        if (!saved)
        {
            var dateQuery = ApplyKeywords(Subscribed(db, uid), filters);
            if (categoryId is { } c2)
                dateQuery = dateQuery.Where(a => a.Source!.Subscriptions.Any(s => s.UserId == uid && s.CategoryId == c2));
            if (sourceId is { } src2)
                dateQuery = dateQuery.Where(a => a.SourceId == src2);
            prev = await dateQuery.Where(a => a.EditionDate < date).MaxAsync(a => (DateOnly?)a.EditionDate, ct);
            next = await dateQuery.Where(a => a.EditionDate > date).MinAsync(a => (DateOnly?)a.EditionDate, ct);
        }

        // The heading is the source's name when reading one source, else the category name.
        var heading = sourceId is { } src3
            ? await db.Subscriptions.Where(s => s.UserId == uid && s.SourceId == src3)
                .Select(s => s.CustomTitle ?? s.Source!.Title).FirstOrDefaultAsync(ct)
            : categoryId is null ? null
                : await db.Categories.Where(c => c.Id == categoryId).Select(c => c.Name).FirstOrDefaultAsync(ct);

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
            CategoryName: saved ? "Saved" : heading,
            Lead: lead,
            Articles: rest,
            Sections: sections);

        return Results.Ok(dto);
    }

    private static async Task<IResult> GetArticle(Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        // Direct open by id deliberately ignores keyword filters (a held link still works).
        var dto = await (
            from a in db.Articles.Where(a => a.Id == id && a.Source!.Subscriptions.Any(s => s.UserId == uid))
            from sub in db.Subscriptions.Where(s => s.UserId == uid && s.SourceId == a.SourceId)
            from st in db.UserArticleStates.Where(s => s.UserId == uid && s.ArticleId == a.Id).DefaultIfEmpty()
            select new ArticleDto(
                a.Id, a.Title, a.Summary, a.ContentHtml, a.Author,
                sub.CustomTitle ?? a.Source!.Title, a.Source!.IconText,
                sub.CategoryId, sub.Category!.Name, sub.Category.Color,
                a.ImageUrl, a.PublishedAt,
                st != null && st.IsRead, st != null && st.IsSaved, st != null ? st.ReadingPositionPercent : 0,
                a.Url))
            .FirstOrDefaultAsync(ct);
        return dto is null ? Results.NotFound() : Results.Ok(dto);
    }

    // ── Per-user state (lazy upsert) ────────────────────────────────────

    /// <summary>Finds the user's state row for an article, creating it if the article is visible.
    /// Returns null when the article isn't visible to the user.</summary>
    private static async Task<UserArticleState?> GetOrCreateStateAsync(
        AppDbContext db, Guid uid, Guid articleId, CancellationToken ct)
    {
        var state = await db.UserArticleStates.FirstOrDefaultAsync(
            s => s.UserId == uid && s.ArticleId == articleId, ct);
        if (state is not null) return state;

        var visible = await db.Articles.AnyAsync(
            a => a.Id == articleId && a.Source!.Subscriptions.Any(s => s.UserId == uid), ct);
        if (!visible) return null;

        state = new UserArticleState { UserId = uid, ArticleId = articleId };
        db.UserArticleStates.Add(state);
        return state;
    }

    private static async Task<IResult> SetRead(Guid id, SetBoolRequest req, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var state = await GetOrCreateStateAsync(db, principal.GetUserId(), id, ct);
        if (state is null) return Results.NotFound();
        state.IsRead = req.Value;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetSaved(Guid id, SetBoolRequest req, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var state = await GetOrCreateStateAsync(db, principal.GetUserId(), id, ct);
        if (state is null) return Results.NotFound();
        state.IsSaved = req.Value;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetPosition(Guid id, SetPositionRequest req, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var state = await GetOrCreateStateAsync(db, principal.GetUserId(), id, ct);
        if (state is null) return Results.NotFound();
        var pct = Math.Clamp(req.Percent, 0, 100);
        state.ReadingPositionPercent = pct;
        if (pct >= 90) state.IsRead = true;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> MarkEditionRead(
        string date, ClaimsPrincipal principal, AppDbContext db, Guid? categoryId, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var d))
            return Results.BadRequest(new { error = "Invalid date." });

        var uid = principal.GetUserId();
        var filters = await LoadFiltersAsync(db, uid, ct);

        var query = ApplyKeywords(Subscribed(db, uid), filters)
            .Where(a => a.EditionDate == d && !a.States.Any(s => s.UserId == uid && s.IsRead));
        if (categoryId is { } cid)
            query = query.Where(a => a.Source!.Subscriptions.Any(s => s.UserId == uid && s.CategoryId == cid));

        var articleIds = await query.Select(a => a.Id).ToListAsync(ct);
        if (articleIds.Count == 0) return Results.Ok(new { marked = 0 });

        // Bulk upsert: flip existing state rows, create rows for never-touched articles.
        var existing = await db.UserArticleStates
            .Where(s => s.UserId == uid && articleIds.Contains(s.ArticleId))
            .ToListAsync(ct);
        var existingIds = existing.Select(s => s.ArticleId).ToHashSet();
        var now = DateTimeOffset.UtcNow;
        foreach (var s in existing) { s.IsRead = true; s.UpdatedAt = now; }
        foreach (var aid in articleIds.Where(i => !existingIds.Contains(i)))
            db.UserArticleStates.Add(new UserArticleState { UserId = uid, ArticleId = aid, IsRead = true, UpdatedAt = now });

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { marked = articleIds.Count });
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
