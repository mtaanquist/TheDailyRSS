using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Services;

/// <summary>Imports and exports OPML subscription lists.</summary>
public sealed class OpmlService(AppDbContext db)
{
    public async Task<string> ExportAsync(Guid userId, CancellationToken ct)
    {
        var categories = await db.Categories
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.SortOrder)
            .Include(c => c.Feeds.OrderBy(f => f.SortOrder))
            .ToListAsync(ct);

        var body = new XElement("body",
            categories.Select(c => new XElement("outline",
                new XAttribute("text", c.Name),
                new XAttribute("title", c.Name),
                c.Feeds.Select(f => new XElement("outline",
                    new XAttribute("type", "rss"),
                    new XAttribute("text", f.Title),
                    new XAttribute("title", f.Title),
                    new XAttribute("xmlUrl", f.FeedUrl),
                    new XAttribute("htmlUrl", f.SiteUrl ?? f.FeedUrl))))));

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("opml",
                new XAttribute("version", "2.0"),
                new XElement("head",
                    new XElement("title", "The Daily RSS subscriptions"),
                    new XElement("dateCreated", DateTimeOffset.UtcNow.ToString("R"))),
                body));

        return doc.Declaration + Environment.NewLine + doc;
    }

    public async Task<OpmlImportResult> ImportAsync(Guid userId, string opml, CancellationToken ct)
    {
        var doc = XDocument.Parse(opml);
        var body = doc.Root?.Element("body");
        if (body is null) return new OpmlImportResult(0, 0, 0);

        var existingCats = await db.Categories
            .Where(c => c.UserId == userId)
            .ToDictionaryAsync(c => c.Name.ToLowerInvariant(), c => c, ct);
        var existingFeedUrls = await db.Feeds
            .Where(f => f.UserId == userId)
            .Select(f => f.FeedUrl)
            .ToHashSetAsync(ct);

        var nextCatOrder = existingCats.Count == 0 ? 0 : existingCats.Values.Max(c => c.SortOrder) + 1;
        int catsCreated = 0, feedsAdded = 0, skipped = 0;

        // Top-level outlines with children are categories; bare feed outlines go to "Imported".
        foreach (var outline in body.Elements("outline"))
        {
            var childFeeds = outline.Elements("outline").Where(IsFeed).ToList();
            if (IsFeed(outline))
            {
                if (AddFeed(userId, GetOrCreateCategory("Imported"), outline)) feedsAdded++; else skipped++;
                continue;
            }

            var name = (outline.Attribute("text") ?? outline.Attribute("title"))?.Value ?? "Imported";
            var category = GetOrCreateCategory(name);
            foreach (var feed in childFeeds)
            {
                if (AddFeed(userId, category, feed)) feedsAdded++; else skipped++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new OpmlImportResult(catsCreated, feedsAdded, skipped);

        Category GetOrCreateCategory(string name)
        {
            var key = name.ToLowerInvariant();
            if (existingCats.TryGetValue(key, out var existing)) return existing;
            var created = new Category { UserId = userId, Name = name, SortOrder = nextCatOrder++ };
            db.Categories.Add(created);
            existingCats[key] = created;
            catsCreated++;
            return created;
        }

        bool AddFeed(Guid uid, Category category, XElement el)
        {
            var xmlUrl = el.Attribute("xmlUrl")?.Value;
            if (string.IsNullOrWhiteSpace(xmlUrl) || !existingFeedUrls.Add(xmlUrl)) return false;
            var title = (el.Attribute("text") ?? el.Attribute("title"))?.Value ?? xmlUrl;
            db.Feeds.Add(new Feed
            {
                UserId = uid,
                Category = category,
                Title = title,
                FeedUrl = xmlUrl,
                SiteUrl = el.Attribute("htmlUrl")?.Value,
                IconText = IconText.From(title),
            });
            return true;
        }
    }

    private static bool IsFeed(XElement el) => el.Attribute("xmlUrl") is not null;
}
