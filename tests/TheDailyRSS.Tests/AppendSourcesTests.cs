using TheDailyRSS.Server.Services;
using Xunit;

namespace TheDailyRSS.Tests;

public class AppendSourcesTests
{
    private static readonly AiSummaryService.SourceRef[] Refs =
    [
        new(1, "First headline", "Reuters", "https://example.com/a"),
        new(2, "Second headline", "BBC", "https://example.com/b"),
        new(3, "Third headline", "The Guardian", "https://example.com/c"),
    ];

    [Fact]
    public void AppendsOnlyCitedSources_InFirstCitationOrder_Deduped()
    {
        var content = "## World\n- **Lead:** something happened [3], confirmed again [3] and elsewhere [1].";
        var result = AiSummaryService.AppendSources(content, Refs);

        Assert.Contains("## Sources", result);
        // Cited 3 then 1; never cited 2.
        var i3 = result.IndexOf("[3] [The Guardian — Third headline](https://example.com/c)");
        var i1 = result.IndexOf("[1] [Reuters — First headline](https://example.com/a)");
        Assert.True(i3 > 0 && i1 > 0);
        Assert.True(i3 < i1);                                  // first-citation order
        Assert.DoesNotContain("Second headline", result);     // [2] was never cited
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(result, @"\] \[The Guardian")); // deduped
    }

    [Fact]
    public void NoCitations_LeavesContentUntouched()
    {
        var content = "## World\n- **Lead:** a quiet day with nothing to footnote.";
        Assert.Equal(content, AiSummaryService.AppendSources(content, Refs));
    }

    [Fact]
    public void StripsBracketsFromLabel_SoLinkSyntaxStaysValid()
    {
        var refs = new[] { new AiSummaryService.SourceRef(1, "Title [with] (brackets)", "Src", "https://example.com/x") };
        var result = AiSummaryService.AppendSources("see [1]", refs);
        // The label must contain no [ ] ( ) so the trailing ](url) is the only link delimiter.
        var line = result[result.IndexOf("- [1]")..];
        var label = line[(line.IndexOf("] [") + 3)..line.IndexOf("](")];
        Assert.DoesNotContain('[', label);
        Assert.DoesNotContain(']', label);
        Assert.DoesNotContain('(', label);
        Assert.DoesNotContain(')', label);
        Assert.Contains("](https://example.com/x)", result);
    }
}
