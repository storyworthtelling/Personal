using Index.Domain.Worksheet;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// Guard G20 (D155, D102): the Snowball payments line multiplies by the Fridays that actually fall in
/// the target month, never a flat 52 &#247; 12 &#8776; 4.33-weeks-a-month average. Watched to fail first
/// against exactly that substitution.
/// </summary>
public class LeftToAssignTests
{
    // 2026: verified real-calendar Friday counts (not asserted from a formula — the whole point is
    // that these must come from the actual calendar).
    [Theory]
    [InlineData(2026, 9, 4)]  // September 2026: Fridays on the 4th, 11th, 18th, 25th
    [InlineData(2026, 10, 5)] // October 2026: Fridays on the 2nd, 9th, 16th, 23rd, 30th — mockup 12's own "5 Fridays"
    public void FridaysInMonth_MatchesTheRealCalendar(int year, int month, int expectedFridays)
    {
        Assert.Equal(expectedFridays, LeftToAssignCalculator.FridaysInMonth(year, month));
        Assert.Equal(expectedFridays, LeftToAssignCalculator.FridaysInMonth($"{year:0000}-{month:00}"));
    }

    [Fact]
    public void AFiveFridayMonth_PaysVisiblyMoreThanAFourFridayMonth_ForTheSameWeeklyTotal()
    {
        const decimal weeklyTotal = 1_250m;

        var fourFridayMonth = LeftToAssignCalculator.Calculate(9_350m, weeklyTotal, fridayCount: 4, lastMonthSpending: 1_906m);
        var fiveFridayMonth = LeftToAssignCalculator.Calculate(9_350m, weeklyTotal, fridayCount: 5, lastMonthSpending: 1_906m);

        Assert.Equal(4 * 1_250, fourFridayMonth.PaymentsWholeDollars);
        Assert.Equal(5 * 1_250, fiveFridayMonth.PaymentsWholeDollars);
        Assert.NotEqual(fourFridayMonth.PaymentsWholeDollars, fiveFridayMonth.PaymentsWholeDollars);

        // A visibly different total, not a rounding-noise difference: the mockup's own worked example
        // (D155) — five Fridays reads $1,250 tighter than four.
        Assert.Equal(1_250, fiveFridayMonth.PaymentsWholeDollars - fourFridayMonth.PaymentsWholeDollars);
    }

    [Fact]
    public void ReintroducedBug_UsingAFlat52Over12WeeksInstead_WouldNeverDistinguishTheTwoMonths()
    {
        // The bug this guard exists to catch: substituting a flat average (52/12 ≈ 4.3333 weeks) for
        // the real Friday count. Both a 4- and a 5-Friday month would then compute the SAME payments
        // figure, which is exactly what LeftToAssignCalculator must never do.
        const decimal weeklyTotal = 1_250m;
        const decimal flatWeeksPerMonth = 52m / 12m;

        var naivePayments = WholeDollars.Ceiling(weeklyTotal * flatWeeksPerMonth);

        var real = LeftToAssignCalculator.Calculate(9_350m, weeklyTotal, LeftToAssignCalculator.FridaysInMonth(2026, 10), 1_906m);

        Assert.NotEqual(naivePayments, real.PaymentsWholeDollars);
    }

    [Fact]
    public void FridaysInMonth_HandlesEveryCalendarMonthLength()
    {
        // 28-, 29-, 30- and 31-day months all resolve without throwing, and February (leap and not)
        // both compute a sane count.
        Assert.InRange(LeftToAssignCalculator.FridaysInMonth(2026, 2), 4, 4);  // Feb 2026: not a leap year
        Assert.InRange(LeftToAssignCalculator.FridaysInMonth(2024, 2), 4, 5);  // Feb 2024: leap year
        Assert.InRange(LeftToAssignCalculator.FridaysInMonth(2026, 1), 4, 5);
    }
}
