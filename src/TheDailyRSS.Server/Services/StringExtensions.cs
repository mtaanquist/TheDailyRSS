using System.Diagnostics.CodeAnalysis;

namespace TheDailyRSS.Server.Services;

public static class StringExtensions
{
    /// <summary>Truncates to at most <paramref name="max"/> characters; null passes through as null.
    /// Used to keep feed-supplied values within their column limits.</summary>
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Truncate(this string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
