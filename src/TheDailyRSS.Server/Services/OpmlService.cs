using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Services;

/// <summary>Imports and exports OPML subscription lists against the shared-source model.</summary>
public sealed class OpmlService(AppDbContext db, FeedSourceService sources)
{
    public async Task<string> ExportAsync(Guid userId, CancellationToken ct)
    {
        var subs = await db.Subscriptions
            .Where(s => s.UserId == userId)
            .Include(s => s.Source)
            .Include(s => s.Category)
            .ToListAsync(ct);

        var body = new XElement("body",
            subs.GroupBy(s => s.Category!)
                .OrderBy(g => g.Key.SortOrder)
                .Select(g => new XElement("outline",
                    new XAttribute("text", g.Key.Name),
                    new XAttribute("title", g.Key.Name),
                    g.OrderBy(s => s.SortOrder).Select(s =>
                    {
                        var title = s.CustomTitle ?? s.Source!.Title;
                        return new XElement("outline",
                            new XAttribute("type", "rss"),
                            new XAttribute("text", title),
                            new XAttribute("title", title),
                            new XAttribute("xmlUrl", s.Source!.FeedUrl),
                            new XAttribute("htmlUrl", s.Source.SiteUrl ?? s.Source.FeedUrl));
                    }))));

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
        // Parse through an XmlReader with DTDs prohibited and no external resolver, so a hostile
        // uploaded OPML can't trigger XXE (external entity / file disclosure) or entity-expansion DoS.
        var doc = ParseSafely(opml);
        var body = doc.Root?.Element("body");
        if (body is null) return new OpmlImportResult(0, 0, 0);

        var cats = await db.Categories.ToListAsync(ct);
        var fallback = cats.First(c => c.Id == CategorySeed.DefaultCategoryId);
        var existingSourceIds = await db.Subscriptions
            .Where(s => s.UserId == userId)
            .Select(s => s.SourceId)
            .ToHashSetAsync(ct);

        int feedsAdded = 0, skipped = 0;

        // Top-level feed outlines go to the default category; folders map to a fixed category by name/slug.
        foreach (var outline in body.Elements("outline"))
        {
            if (IsFeed(outline))
            {
                if (await AddFeed(fallback, outline)) feedsAdded++; else skipped++;
                continue;
            }

            var name = (outline.Attribute("text") ?? outline.Attribute("title"))?.Value;
            var category = MapCategory(name);
            foreach (var feed in outline.Elements("outline").Where(IsFeed))
            {
                if (await AddFeed(category, feed)) feedsAdded++; else skipped++;
            }
        }

        await db.SaveChangesAsync(ct);
        // Categories are fixed/seeded, so none are ever created by import.
        return new OpmlImportResult(0, feedsAdded, skipped);

        Category MapCategory(string? name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                var key = name.Trim().ToLowerInvariant();
                var match = cats.FirstOrDefault(c => c.Name.ToLowerInvariant() == key || c.Slug == key);
                if (match is not null) return match;
            }
            return fallback;
        }

        async Task<bool> AddFeed(Category category, XElement el)
        {
            var xmlUrl = el.Attribute("xmlUrl")?.Value;
            if (string.IsNullOrWhiteSpace(xmlUrl)) return false;
            var title = (el.Attribute("text") ?? el.Attribute("title"))?.Value ?? xmlUrl;
            var siteUrl = el.Attribute("htmlUrl")?.Value;

            var (source, _) = await sources.GetOrCreateAsync(xmlUrl, title, siteUrl, ct);
            if (!existingSourceIds.Add(source.Id)) return false; // already subscribed / duplicate in file

            db.Subscriptions.Add(new Subscription
            {
                UserId = userId,
                SourceId = source.Id,
                CategoryId = category.Id,
                CustomTitle = source.Title == title ? null : title,
            });
            return true;
        }
    }

    private static bool IsFeed(XElement el) => el.Attribute("xmlUrl") is not null;

    private static XDocument ParseSafely(string opml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024,
        };
        using var stringReader = new StringReader(opml);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(xmlReader);
    }
}
