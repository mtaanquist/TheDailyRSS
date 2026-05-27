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

    /// <summary>Drops articles the user has hidden. Applied everywhere except the Hidden view itself.</summary>
    public static IQueryable<Article> NotHidden(IQueryable<Article> q, Guid uid) =>
        q.Where(a => !a.States.Any(s => s.UserId == uid && s.IsHidden));

    /// <summary>Drops articles matching any of the user's mute terms. Bare terms match whole words;
    /// a <c>*</c> wildcard matches partials (see <see cref="KeywordMatching"/>). Postgres runs the
    /// regex server-side via the case-insensitive <c>~*</c> operator.</summary>
    public static IQueryable<Article> ApplyKeywords(IQueryable<Article> q, List<KeywordFilter> filters)
    {
        foreach (var f in filters)
        {
            var pattern = KeywordMatching.BuildPattern(f.Term);
            if (pattern is null) continue;
            if (f.Scope == KeywordScope.TitleOnly)
                q = q.Where(a => !Regex.IsMatch(a.Title, pattern, RegexOptions.IgnoreCase));
            else
                q = q.Where(a => !Regex.IsMatch(a.Title, pattern, RegexOptions.IgnoreCase)
                    && !(a.Summary != null && Regex.IsMatch(a.Summary, pattern, RegexOptions.IgnoreCase))
                    && !(a.ContentHtml != null && Regex.IsMatch(a.ContentHtml, pattern, RegexOptions.IgnoreCase)));
        }
        return q;
    }

    public static async Task<List<KeywordFilter>> LoadFiltersAsync(AppDbContext db, Guid uid, CancellationToken ct) =>
        await db.KeywordFilters.Where(k => k.UserId == uid).ToListAsync(ct);
}
