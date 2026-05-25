using TheDailyRSS.Server.Data;
using TheDailyRSS.Shared;

namespace TheDailyRSS.Server.Endpoints;

public static class Mappers
{
    public static string Initials(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (char.ToUpperInvariant(parts[0][0]).ToString() + char.ToUpperInvariant(parts[^1][0])),
        };
    }

    public static UserDto ToDto(this AppUser u) => new(
        u.Id,
        u.Email ?? "",
        u.DisplayName,
        Initials(u.DisplayName),
        u.CreatedAt,
        new PreferencesDto { Theme = u.Theme, HeadlineFont = u.HeadlineFont, Density = u.Density });

    public static SessionDto ToDto(this UserSession s, Guid currentSessionId) => new(
        s.Id, s.DeviceLabel, s.UserAgent, s.IpAddress, s.CreatedAt, s.LastSeenAt, s.Id == currentSessionId);

    public static ArticleSummaryDto ToSummary(this Article a) => new(
        a.Id, a.Title, a.Summary,
        a.Feed!.Title, a.Feed.IconText,
        a.Feed.CategoryId, a.Feed.Category!.Name, a.Feed.Category.Color,
        a.ImageUrl, a.PublishedAt, a.IsRead, a.IsSaved, a.Url);

    public static ArticleDto ToDto(this Article a) => new(
        a.Id, a.Title, a.Summary, a.ContentHtml, a.Author,
        a.Feed!.Title, a.Feed.IconText,
        a.Feed.CategoryId, a.Feed.Category!.Name, a.Feed.Category.Color,
        a.ImageUrl, a.PublishedAt, a.IsRead, a.IsSaved, a.ReadingPositionPercent, a.Url);
}
