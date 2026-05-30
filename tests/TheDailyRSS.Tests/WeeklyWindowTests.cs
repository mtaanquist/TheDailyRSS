using TheDailyRSS.Server.Services;
using Xunit;

namespace TheDailyRSS.Tests;

public class WeeklyWindowTests
{
    /// <summary>For every day across a fortnight, "The Weekly" window must be a Monday–Sunday run
    /// ending on the most recent (or same-day) Sunday — that's the week it covers, curated Sunday morning.</summary>
    [Fact]
    public void WeeklyWindow_IsMondayToTheMostRecentSunday()
    {
        var start = new DateOnly(2026, 5, 18); // a Monday
        for (var i = 0; i < 14; i++)
        {
            var today = start.AddDays(i);
            var (s, e) = AiSummaryService.WeeklyWindow(today);

            Assert.Equal(DayOfWeek.Monday, s.DayOfWeek);
            Assert.Equal(DayOfWeek.Sunday, e.DayOfWeek);
            Assert.Equal(6, e.DayNumber - s.DayNumber);          // a 7-day run
            Assert.True(e <= today);                             // never reaches into the future
            Assert.True(today.DayNumber - e.DayNumber < 7);      // it's the *most recent* Sunday
        }
    }

    [Fact]
    public void WeeklyWindow_OnSunday_CoversThatSameWeek()
    {
        var sunday = new DateOnly(2026, 5, 24);
        Assert.Equal(DayOfWeek.Sunday, sunday.DayOfWeek);

        var (s, e) = AiSummaryService.WeeklyWindow(sunday);
        Assert.Equal(new DateOnly(2026, 5, 18), s);
        Assert.Equal(sunday, e);
    }

    [Fact]
    public void WeeklyWindow_OnMonday_CoversThePreviousWeek()
    {
        var monday = new DateOnly(2026, 5, 25);
        Assert.Equal(DayOfWeek.Monday, monday.DayOfWeek);

        var (s, e) = AiSummaryService.WeeklyWindow(monday);
        Assert.Equal(new DateOnly(2026, 5, 18), s);
        Assert.Equal(new DateOnly(2026, 5, 24), e);
    }
}
