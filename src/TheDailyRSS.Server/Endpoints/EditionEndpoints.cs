using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using TheDailyRSS.Server.Auth;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Services;
using TheDailyRSS.Shared;
using static TheDailyRSS.Server.Services.ArticleQueries;

namespace TheDailyRSS.Server.Endpoints;

public static class EditionEndpoints
{
    /// <summary>How many articles a category contributes to the curated front page: 5–8, varied per
    /// section and per day for a more newspapery layout. Deterministic in (date, category) — uses a stable
    /// hash (not <see cref="HashCode"/>, which is per-process randomized) so the count stays the same across
    /// refreshes and restarts of the same edition.</summary>
    private static int FrontPageSectionSize(DateOnly date, Guid categoryId)
    {
        var h = unchecked((uint)(date.DayNumber * 92821) ^ (uint)categoryId.GetHashCode());
        return 5 + (int)(h % 4);
    }

    /// <summary>Upper bound on articles materialised for a single edition view.</summary>
    private const int MaxEditionArticles = 300;

    /// <summary>De-dup group key: collapses articles that share a source URL, while keeping URL-less
    /// items distinct (by id) so they're never merged together.</summary>
    private static string DedupKey(ArticleSummaryDto s) => string.IsNullOrEmpty(s.Url) ? s.Id.ToString() : s.Url;

    public static void MapEditionEndpoints(this IEndpointRouteBuilder app)
    {
        var editions = app.MapGroup("/api/editions").RequireAuthorization();
        editions.MapGet("/dates", ListDates);
        editions.MapGet("/latest", Latest);
        editions.MapGet("/{date}", GetEdition);
        editions.MapPost("/{date}/mark-read", MarkEditionRead);

        var articles = app.MapGroup("/api/articles").RequireAuthorization();
        articles.MapGet("/{id:guid}", GetArticle);
        articles.MapGet("/{id:guid}/neighbors", GetNeighbors);
        articles.MapPost("/{id:guid}/read", SetRead);
        articles.MapPost("/{id:guid}/save", SetSaved);
        articles.MapPost("/{id:guid}/hide", SetHidden);
        articles.MapPost("/{id:guid}/position", SetPosition);
        articles.MapPost("/{id:guid}/summary", SummarizeArticle);
    }

    // ── Visibility, keyword & projection helpers live in ArticleQueries (shared with AI summaries) ──

    // ── Reads ───────────────────────────────────────────────────────────

    private static async Task<IResult> ListDates(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        var visible = WithContent(await VisibleAsync(db, uid, ct));

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
        var latestQuery = NotHidden(Subscribed(db, uid), uid);
        if (sourceId is { } sid) latestQuery = latestQuery.Where(a => a.SourceId == sid);
        var latest = await latestQuery.MaxAsync(a => (DateOnly?)a.EditionDate, ct);
        var date = latest ?? EditionClock.Today(opts.Value);
        return await BuildEdition(uid, date, categoryId, sourceId, saved: false, hidden: false, unreadOnly ?? false, db, opts.Value, ct);
    }

    private static async Task<IResult> GetEdition(
        string date, ClaimsPrincipal principal, AppDbContext db, IOptions<FeedOptions> opts,
        Guid? categoryId, Guid? sourceId, bool? saved, bool? hidden, bool? unreadOnly, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var d))
            return ApiResults.Fail("Invalid date.");
        return await BuildEdition(principal.GetUserId(), d, categoryId, sourceId, saved ?? false, hidden ?? false, unreadOnly ?? false, db, opts.Value, ct);
    }

    private static async Task<IResult> BuildEdition(
        Guid uid, DateOnly date, Guid? categoryId, Guid? sourceId, bool saved, bool hidden, bool unreadOnly,
        AppDbContext db, FeedOptions opts, CancellationToken ct)
    {
        var keywords = await LoadFiltersAsync(db, uid, ct);
        var fieldFilters = await LoadFieldFiltersAsync(db, uid, ct);
        // Load the mute filters once; the muted set is reused for the listing, the unread total and
        // the prev/next probing below.
        var muted = ApplyMutes(db, uid, keywords, fieldFilters);

        // "Saved" and "Hidden" are cross-date pseudo-sections; everything else is bound to the day.
        // Hidden articles are dropped from every view except the Hidden list itself.
        var query = hidden ? muted.Where(a => a.States.Any(s => s.UserId == uid && s.IsHidden))
            : saved ? NotHidden(muted, uid).Where(a => a.States.Any(s => s.UserId == uid && s.IsSaved))
            : WithContent(NotHidden(muted, uid)).Where(a => a.EditionDate == date);
        query = Narrow(query, uid, categoryId, sourceId);
        if (unreadOnly)
            query = query.Where(a => !a.States.Any(s => s.UserId == uid && s.IsRead));

        var top = query.OrderByDescending(a => a.PublishedAt).Take(MaxEditionArticles);
        var summaries = (await ToSummaries(top, db, uid).ToListAsync(ct))
            .OrderByDescending(s => s.PublishedAt)
            // Collapse stories that link to the same source article — a feed that lists an item twice, or
            // the same story carried by two subscribed feeds — keeping the first (most recent) copy.
            .GroupBy(s => DedupKey(s))
            .Select(g => g.First())
            .ToList();

        // Lead: newest article that has an image, otherwise just the newest.
        var lead = summaries.FirstOrDefault(s => s.ImageUrl is not null) ?? summaries.FirstOrDefault();
        var rest = summaries.Where(s => lead is null || s.Id != lead.Id).ToList();

        var isFrontPage = categoryId is null && sourceId is null && !saved && !hidden;
        var sections = await BuildSectionsAsync(db, date, isFrontPage, rest, summaries, ct);

        // The masthead unread count reflects the edition being viewed, not all of time — otherwise
        // unread from older days makes "today" feel overwhelming and "mark all read" never zeroes it.
        // Saved/Hidden are cross-date pseudo-sections, so they keep counting across every day.
        // Counts distinct source URLs so duplicates collapse to one, matching the de-duped listing above.
        var unreadBase = NotHidden(muted, uid).Where(a => !a.States.Any(s => s.UserId == uid && s.IsRead));
        if (!saved && !hidden)
            unreadBase = WithContent(unreadBase).Where(a => a.EditionDate == date);
        var unreadTotal = await unreadBase.Select(a => a.Url).Distinct().CountAsync(ct);

        DateOnly? prev = null, next = null;
        if (!saved && !hidden)
            (prev, next) = await ResolveAdjacentDatesAsync(NotHidden(muted, uid), uid, date, categoryId, sourceId, ct);

        var heading = await ResolveHeadingAsync(db, uid, categoryId, sourceId, ct);

        var dto = new EditionDto(
            Date: date,
            VolumeLabel: Masthead.Volume(date),
            IssueLabel: Masthead.Issue(date),
            DateLabel: Masthead.DateLabel(date),
            IsToday: date == EditionClock.Today(opts),
            PrevDate: prev,
            NextDate: next,
            UnreadTotal: unreadTotal,
            CategoryId: categoryId,
            CategoryName: hidden ? "Hidden" : saved ? "Saved" : heading,
            Lead: lead,
            Articles: rest,
            Sections: sections);

        return Results.Ok(dto);
    }

    /// <summary>Applies the optional category and source narrowing shared by the listing query and
    /// the prev/next date probe.</summary>
    private static IQueryable<Article> Narrow(IQueryable<Article> q, Guid uid, Guid? categoryId, Guid? sourceId)
    {
        if (categoryId is { } cid)
            q = q.Where(a => a.Source!.Subscriptions.Any(s => s.UserId == uid && s.CategoryId == cid));
        if (sourceId is { } src)
            q = q.Where(a => a.SourceId == src);
        return q;
    }

    /// <summary>Groups summaries into category sections. The curated front page caps each section to
    /// a per-day slice (over <paramref name="rest"/>, i.e. excluding the lead) and orders by taxonomy;
    /// a drill-down keeps the full flat list.</summary>
    private static async Task<List<EditionSectionDto>> BuildSectionsAsync(
        AppDbContext db, DateOnly date, bool isFrontPage,
        List<ArticleSummaryDto> rest, List<ArticleSummaryDto> summaries, CancellationToken ct)
    {
        if (!isFrontPage)
            return summaries
                .GroupBy(s => (s.CategoryId, s.CategoryName, s.CategoryColor))
                .Select(g => new EditionSectionDto(
                    g.Key.CategoryId, g.Key.CategoryName, g.Key.CategoryColor, g.Count(), g.ToList()))
                .ToList();

        var order = await db.Categories.ToDictionaryAsync(c => c.Id, c => c.SortOrder, ct);
        return rest
            .GroupBy(s => (s.CategoryId, s.CategoryName, s.CategoryColor))
            .Select(g => new EditionSectionDto(
                g.Key.CategoryId, g.Key.CategoryName, g.Key.CategoryColor,
                g.Count(), g.Take(FrontPageSectionSize(date, g.Key.CategoryId)).ToList()))
            .OrderBy(s => order.TryGetValue(s.CategoryId, out var o) ? o : int.MaxValue)
            .ToList();
    }

    /// <summary>The previous/next edition dates that have content, honouring the active category/source.</summary>
    private static async Task<(DateOnly? Prev, DateOnly? Next)> ResolveAdjacentDatesAsync(
        IQueryable<Article> visible, Guid uid, DateOnly date, Guid? categoryId, Guid? sourceId, CancellationToken ct)
    {
        var q = Narrow(visible, uid, categoryId, sourceId);
        var prev = await q.Where(a => a.EditionDate < date).MaxAsync(a => (DateOnly?)a.EditionDate, ct);
        var next = await q.Where(a => a.EditionDate > date).MinAsync(a => (DateOnly?)a.EditionDate, ct);
        return (prev, next);
    }

    /// <summary>The masthead heading: the source's title when reading one source, else the category
    /// name (null on the all-category front page).</summary>
    private static async Task<string?> ResolveHeadingAsync(
        AppDbContext db, Guid uid, Guid? categoryId, Guid? sourceId, CancellationToken ct) =>
        sourceId is { } src
            ? await db.Subscriptions.Where(s => s.UserId == uid && s.SourceId == src)
                .Select(s => s.CustomTitle ?? s.Source!.Title).FirstOrDefaultAsync(ct)
            : categoryId is null ? null
                : await db.Categories.Where(c => c.Id == categoryId).Select(c => c.Name).FirstOrDefaultAsync(ct);

    private static async Task<IResult> GetArticle(
        Guid id, ClaimsPrincipal principal, AppDbContext db, HtmlSanitizationService sanitizer, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        // Direct open by id deliberately ignores keyword/field filters (a held link still works).
        // Project to an intermediate shape so the JSONB Fields can be reshaped into the
        // DTO's read-only dictionary on the client side of the boundary.
        var row = await (
            from a in db.Articles.Where(a => a.Id == id && a.Source!.Subscriptions.Any(s => s.UserId == uid))
            from sub in db.Subscriptions.Where(s => s.UserId == uid && s.SourceId == a.SourceId)
            from st in db.UserArticleStates.Where(s => s.UserId == uid && s.ArticleId == a.Id).DefaultIfEmpty()
            select new
            {
                a.Id, a.Title, a.Summary, a.ContentHtml,
                a.FullContentHtml, FetchFull = a.Source!.FetchFullContent,
                a.Author,
                FeedTitle = sub.CustomTitle ?? a.Source!.Title,
                a.Source!.IconText,
                sub.CategoryId,
                CategoryName = sub.Category!.Name,
                CategoryColor = sub.Category.Color,
                a.ImageUrl, a.PublishedAt,
                IsRead = st != null && st.IsRead,
                IsSaved = st != null && st.IsSaved,
                IsHidden = st != null && st.IsHidden,
                ReadingPositionPercent = st != null ? st.ReadingPositionPercent : 0,
                a.Url, a.SourceId, a.Fields,
                AiSummary = db.ArticleSummaries
                    .Where(s => s.UserId == uid && s.ArticleId == a.Id)
                    .Select(s => s.Content).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return Results.NotFound();

        var fields = (IReadOnlyDictionary<string, IReadOnlyList<string>>)row.Fields
            .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);

        // Prefer the reader-mode extraction when the source has it on and we have a usable body
        // ("" means we tried and got nothing); otherwise serve the feed's own content. Sanitized either way.
        var useFullContent = row.FetchFull && !string.IsNullOrEmpty(row.FullContentHtml);
        var body = useFullContent ? row.FullContentHtml : row.ContentHtml;

        // Strip body images when: the reader-mode body duplicates the hero we render from ImageUrl, or the
        // reader has turned on "no pictures" mode (#41). Either way the source HTML keeps its images stored.
        var hideImages = await db.Users.Where(u => u.Id == uid).Select(u => u.HideImages).FirstOrDefaultAsync(ct);
        return Results.Ok(new ArticleDto(
            row.Id, row.Title, row.Summary, sanitizer.Sanitize(body, stripImages: useFullContent || hideImages), row.Author,
            row.FeedTitle, row.IconText,
            row.CategoryId, row.CategoryName, row.CategoryColor,
            row.ImageUrl, row.PublishedAt,
            row.IsRead, row.IsSaved, row.IsHidden, row.ReadingPositionPercent,
            row.Url, row.SourceId, fields, row.AiSummary));
    }

    /// <summary>Generates (or regenerates) the reader's AI TL;DR for one article, on demand, using their
    /// own BYOK endpoint. The article must belong to a source they subscribe to.</summary>
    private static async Task<IResult> SummarizeArticle(
        Guid id, ClaimsPrincipal principal, AppDbContext db, AiSummaryService ai, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        var user = await db.Users.FindAsync([uid], ct);
        if (user is null) return Results.Unauthorized();

        var article = await db.Articles
            .FirstOrDefaultAsync(a => a.Id == id && a.Source!.Subscriptions.Any(s => s.UserId == uid), ct);
        if (article is null) return Results.NotFound();

        try { return Results.Ok(await ai.SummarizeArticleAsync(user, article, ct)); }
        catch (AiException ex) { return ApiResults.Fail(ex.Message); }
    }

    /// <summary>The previous/next stories in the same edition (day), in the same order the edition grid
    /// uses (newest first). Keyword filters apply so neighbours match what the reader actually browses.</summary>
    private static async Task<IResult> GetNeighbors(Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var uid = principal.GetUserId();
        var editionDate = await Subscribed(db, uid)
            .Where(a => a.Id == id)
            .Select(a => (DateOnly?)a.EditionDate)
            .FirstOrDefaultAsync(ct);
        if (editionDate is not { } day) return Results.NotFound();

        var visible = await VisibleAsync(db, uid, ct);
        var ordered = await visible
            .Where(a => a.EditionDate == day)
            .OrderByDescending(a => a.PublishedAt).ThenBy(a => a.Id)
            .Select(a => new ArticleLinkDto(a.Id, a.Title))
            .ToListAsync(ct);

        var i = ordered.FindIndex(a => a.Id == id);
        var prev = i > 0 ? ordered[i - 1] : null;
        var next = i >= 0 && i < ordered.Count - 1 ? ordered[i + 1] : null;
        return Results.Ok(new ArticleNeighborsDto(prev, next));
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

    private static Task<IResult> SetRead(Guid id, SetBoolRequest req, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        MutateStateAsync(db, principal.GetUserId(), id, s => s.IsRead = req.Value, ct);

    private static Task<IResult> SetSaved(Guid id, SetBoolRequest req, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        MutateStateAsync(db, principal.GetUserId(), id, s => s.IsSaved = req.Value, ct);

    private static Task<IResult> SetHidden(Guid id, SetBoolRequest req, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        MutateStateAsync(db, principal.GetUserId(), id, s => s.IsHidden = req.Value, ct);

    private static Task<IResult> SetPosition(Guid id, SetPositionRequest req, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        MutateStateAsync(db, principal.GetUserId(), id, s =>
        {
            var pct = Math.Clamp(req.Percent, 0, 100);
            s.ReadingPositionPercent = pct;
            if (pct >= 90) s.IsRead = true;
        }, ct);

    /// <summary>Applies a single-article state change, retrying on a lost race with another device
    /// (see <see cref="ConcurrencyRetryExtensions.ExecuteWithRetryAsync"/>).</summary>
    private static Task<IResult> MutateStateAsync(
        AppDbContext db, Guid uid, Guid articleId, Action<UserArticleState> apply, CancellationToken ct) =>
        db.ExecuteWithRetryAsync<IResult>(async () =>
        {
            var state = await GetOrCreateStateAsync(db, uid, articleId, ct);
            if (state is null) return Results.NotFound();
            apply(state);
            state.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

    private static async Task<IResult> MarkEditionRead(
        string date, ClaimsPrincipal principal, AppDbContext db, Guid? categoryId, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var d))
            return ApiResults.Fail("Invalid date.");

        var uid = principal.GetUserId();
        var visible = WithContent(await VisibleAsync(db, uid, ct));
        var query = visible
            .Where(a => a.EditionDate == d && !a.States.Any(s => s.UserId == uid && s.IsRead));
        if (categoryId is { } cid)
            query = query.Where(a => a.Source!.Subscriptions.Any(s => s.UserId == uid && s.CategoryId == cid));

        var articleIds = await query.Select(a => a.Id).ToListAsync(ct);
        if (articleIds.Count == 0) return Results.Ok(new { marked = 0 });

        // Bulk upsert: flip existing state rows, create rows for never-touched articles. Retry on a
        // concurrent write from another device (xmin conflict or a racing insert of the same row).
        return await db.ExecuteWithRetryAsync<IResult>(async () =>
        {
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
        });
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
