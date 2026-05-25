using System.Text;

namespace TheDailyRSS.Server.Endpoints;

/// <summary>
/// Turns a user's mute term into a PostgreSQL regular expression (used with the
/// case-insensitive <c>~*</c> operator).
///
/// <para>A bare term matches as whole words — <c>buy</c> won't hit "buyer", and
/// <c>where to buy</c> matches that phrase. A <c>*</c> acts as a wildcard for partial
/// matches — <c>*buy*</c> hits "buyer", <c>deal*</c> hits "deals".</para>
/// </summary>
public static class KeywordMatching
{
    /// <summary>Builds the Postgres regex for a term, or <c>null</c> if the term has no literal
    /// content to match on (e.g. <c>"***"</c>) — such a term should be ignored / rejected.</summary>
    public static string? BuildPattern(string term)
    {
        term = term.Trim();
        if (term.Length == 0) return null;

        if (term.Contains('*'))
        {
            var parts = term.Split('*');
            if (parts.All(p => p.Length == 0)) return null; // only wildcards → would match everything
            return string.Join(".*", parts.Select(Escape));
        }

        // No wildcard → anchor to word edges so it matches the term as whole word(s).
        return $@"\m{Escape(term)}\M";
    }

    /// <summary>Escapes the POSIX-regex metacharacters in a literal piece (spaces are left intact).</summary>
    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c is '\\' or '.' or '^' or '$' or '|' or '?' or '+' or '*' or '(' or ')' or '[' or ']' or '{' or '}')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
