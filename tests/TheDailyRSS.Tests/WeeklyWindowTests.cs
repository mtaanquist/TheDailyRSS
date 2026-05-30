using TheDailyRSS.Server.Services;
using Xunit;

namespace TheDailyRSS.Tests;

public class WeeklyWindowTests
{
    /// <summary>For every day across a fortnight, "The Weekly" window must be a Monday–Saturday run
    /// ending on the most recent (or same-day) Saturday — the six days a Sunday paper reports on,
    /// curated Saturday night.</summary>
    [Fact]
    public void WeeklyWindow_IsMondayToTheMostRecentSaturday()
    {
        var start = new DateOnly(2026, 5, 18); // a Monday
        for (var i = 0; i < 14; i++)
        {
            var today = start.AddDays(i);
            var (s, e) = AiSummaryService.WeeklyWindow(today);

            Assert.Equal(DayOfWeek.Monday, s.DayOfWeek);
            Assert.Equal(DayOfWeek.Saturday, e.DayOfWeek);
            Assert.Equal(5, e.DayNumber - s.DayNumber);          // Mon–Sat is a 6-day run
            Assert.True(e <= today);                             // never reaches into the future
            Assert.True(today.DayNumber - e.DayNumber < 7);      // it's the *most recent* Saturday
        }
    }

    [Fact]
    public void WeeklyWindow_OnSaturday_CoversThatSameWeek()
    {
        var saturday = new DateOnly(2026, 5, 23);
        Assert.Equal(DayOfWeek.Saturday, saturday.DayOfWeek);

        var (s, e) = AiSummaryService.WeeklyWindow(saturday);
        Assert.Equal(new DateOnly(2026, 5, 18), s); // Monday
        Assert.Equal(saturday, e);
    }

    [Fact]
    public void WeeklyWindow_OnSunday_CoversTheWeekJustEnded()
    {
        var sunday = new DateOnly(2026, 5, 24);
        Assert.Equal(DayOfWeek.Sunday, sunday.DayOfWeek);

        var (s, e) = AiSummaryService.WeeklyWindow(sunday);
        Assert.Equal(new DateOnly(2026, 5, 18), s); // previous Monday
        Assert.Equal(new DateOnly(2026, 5, 23), e); // yesterday (Saturday)
    }
}
