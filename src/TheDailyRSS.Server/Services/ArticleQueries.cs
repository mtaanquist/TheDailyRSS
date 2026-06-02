using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Server.Endpoints;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Services;

/// <summary>Shared per-user article visibility rules — subscription scope, hidden state, and
/// muted keywords. Used by both the edition endpoints and AI summary generation so a digest
/// covers exactly what the reader would see in their edition.</summary>
public static class ArticleQueries
{
    /// <summary>Articles the user can see: those belonging to a source they subscribe to.</summary>
    public static IQueryable<Article> Subscribed(AppDbContext db, Guid uid) =>
        db.Articles.Where(a => a.Source!.Subscriptions.Any(s => s.UserId == uid));

    /// <summary>Projects subscribed articles to per-user summaries, resolving the per-user category and
    /// read/saved/hidden state. Shared by the edition endpoints and the Weekly curation so both views
    /// see identical headlines.</summary>
    public static IQueryable<ArticleSummaryDto> ToSummaries(IQueryable<Article> articles, AppDbContext db, Guid uid) =>
        from a in articles
        from sub in db.Subscriptions.Where(s => s.UserId == uid && s.SourceId == a.SourceId)
        from st in db.UserArticleStates.Where(s => s.UserId == uid && s.ArticleId == a.Id).DefaultIfEmpty()
        select new ArticleSummaryDto(
            a.Id, a.Title, a.Summary,
            sub.CustomTitle ?? a.Source!.Title, a.Source!.IconText,
            sub.CategoryId, sub.Category!.Name, sub.Category.Color,
            a.ImageUrl, a.PublishedAt,
            st != null && st.IsRead, st != null && st.IsSaved, st != null && st.IsHidden, a.Url);

    /// <summary>The reader's visible article set — subscribed sources with keyword and field mutes
    /// applied (and, unless <paramref name="includeHidden"/>, hidden articles dropped). Loads both
    /// filter lists and composes them in the one correct order, so call sites don't re-assemble the
    /// pipeline by hand. Use <paramref name="includeHidden"/> = true for the Hidden view.</summary>
    public static async Task<IQueryable<Article>> VisibleAsync(
        AppDbContext db, Guid uid, CancellationToken ct, bool includeHidden = false)
    {
        var keywords = await LoadFiltersAsync(db, uid, ct);
        var fields = await LoadFieldFiltersAsync(db, uid, ct);
        var q = ApplyMutes(db, uid, keywords, fields);
        return includeHidden ? q : NotHidden(q, uid);
    }

    /// <summary>Composes the subscribed set with already-loaded keyword + field mutes (no hidden
    /// filter). Lets a caller that needs the muted set several times load the filters just once.</summary>
    public static IQueryable<Article> ApplyMutes(
        AppDbContext db, Guid uid, List<KeywordFilter> keywords, List<FieldFilter> fields) =>
        ApplyFieldFilters(ApplyKeywords(Subscribed(db, uid), keywords), fields);

    /// <summary>Drops articles the user has hidden. Applied everywhere except the Hidden view itself.</summary>
    public static IQueryable<Article> NotHidden(IQueryable<Article> q, Guid uid) =>
        q.Where(a => !a.States.Any(s => s.UserId == uid && s.IsHidden));

    /// <summary>Keeps only articles that carry some readable text — a summary or a body. A feed item that's
    /// purely a video embed or a live-thread placeholder (no summary, no content) is dropped from editions
    /// and their counts. <see cref="Article.FullContentHtml"/> is the reader-mode extraction, where "" means
    /// "tried and got nothing", so an empty string doesn't count as content.</summary>
    public static IQueryable<Article> WithContent(IQueryable<Article> q) =>
        q.Where(a => (a.Summary != null && a.Summary != "")
                  || (a.ContentHtml != null && a.ContentHtml != "")
                  || (a.FullContentHtml != null && a.FullContentHtml != ""));

    /// <summary>Drops articles matching any of the user's mute terms. Bare terms match whole words;
    /// a <c>*</c> wildcard matches partials (see <see cref="KeywordMatching"/>). Postgres runs the
    /// regex server-side via the case-insensitive <c>~*</c> operator. A filter with
    /// <see cref="KeywordFilter.SourceId"/> set only matches articles from that feed.</summary>
    public static IQueryable<Article> ApplyKeywords(IQueryable<Article> q, List<KeywordFilter> filters)
    {
        foreach (var f in filters)
        {
            var pattern = KeywordMatching.BuildPattern(f.Term);
            if (pattern is null) continue;
            var sid = f.SourceId;
            if (f.Scope == KeywordScope.TitleOnly)
                q = q.Where(a => (sid.HasValue && a.SourceId != sid.Value)
                    || !Regex.IsMatch(a.Title, pattern, RegexOptions.IgnoreCase));
            else
                q = q.Where(a => (sid.HasValue && a.SourceId != sid.Value)
                    || (!Regex.IsMatch(a.Title, pattern, RegexOptions.IgnoreCase)
                        && !(a.Summary != null && Regex.IsMatch(a.Summary, pattern, RegexOptions.IgnoreCase))
                        && !(a.ContentHtml != null && Regex.IsMatch(a.ContentHtml, pattern, RegexOptions.IgnoreCase))));
        }
        return q;
    }

    public static async Task<List<KeywordFilter>> LoadFiltersAsync(AppDbContext db, Guid uid, CancellationToken ct) =>
        await db.KeywordFilters.Where(k => k.UserId == uid).ToListAsync(ct);

    public static async Task<List<FieldFilter>> LoadFieldFiltersAsync(AppDbContext db, Guid uid, CancellationToken ct) =>
        await db.FieldFilters.Where(f => f.UserId == uid).ToListAsync(ct);

    /// <summary>Drops articles whose JSONB <c>Fields</c> column contains any of the user's
    /// (field, value) mute rules. Translates each rule to the Postgres <c>@&gt;</c> operator via
    /// Npgsql's <see cref="NpgsqlDbFunctionsExtensions.JsonContains"/>; rules can be optionally
    /// scoped to a single feed source.</summary>
    public static IQueryable<Article> ApplyFieldFilters(IQueryable<Article> q, List<FieldFilter> filters)
    {
        foreach (var f in filters)
        {
            if (f.Operator != FieldFilterOperator.Equals) continue;     // future operators land here
            if (string.IsNullOrEmpty(f.FieldKey) || string.IsNullOrEmpty(f.Value)) continue;

            // { "<key>": ["<value>"] } — JSON containment matches when the array on the LHS
            // contains the value as an element, which is what we want for a single-value rule.
            var json = JsonSerializer.Serialize(
                new Dictionary<string, string[]> { [f.FieldKey] = new[] { f.Value } });

            if (f.SourceId is { } sid)
                q = q.Where(a => a.SourceId != sid || !EF.Functions.JsonContains(a.Fields, json));
            else
                q = q.Where(a => !EF.Functions.JsonContains(a.Fields, json));
        }
        return q;
    }
}
