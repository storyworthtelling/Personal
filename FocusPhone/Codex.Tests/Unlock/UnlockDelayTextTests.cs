using Codex.Domain.Unlock;
using Xunit;

namespace Codex.Tests.Unlock;

public class UnlockDelayTextTests
{
    [Theory]
    [InlineData("1 hour", 1)]
    [InlineData("7 hours", 7)]
    [InlineData("6", 6)]
    [InlineData("6h", 6)]
    [InlineData("10 days", 240)]
    [InlineData("1 day", 24)]
    [InlineData("2 weeks", 336)]
    [InlineData("25 months", 25 * 30 * 24)]
    [InlineData("1 month", 30 * 24)]
    [InlineData("  3   D  ", 72)]
    [InlineData("2 HOURS", 2)]
    public void TryParse_AcceptsAnyWholeNumberWithAUnit(string text, int expectedHours)
    {
        Assert.True(UnlockDelayText.TryParse(text, out var delay));
        Assert.Equal(TimeSpan.FromHours(expectedHours), delay);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ten days")]
    [InlineData("0 hours")]
    [InlineData("-3 days")]
    [InlineData("1.5 hours")]
    [InlineData("5 fortnights")]
    [InlineData("10 days 3 hours")]
    [InlineData("99999999999 months")]
    public void TryParse_RejectsWhatItCannotRead(string text)
    {
        Assert.False(UnlockDelayText.TryParse(text, out _));
    }

    [Theory]
    [InlineData(1, "1 hour")]
    [InlineData(6, "6 hours")]
    [InlineData(24, "1 day")]
    [InlineData(240, "10 days")]
    [InlineData(336, "14 days")]
    [InlineData(720, "1 month")]
    [InlineData(25 * 30 * 24, "25 months")]
    public void Format_ReadsBackInTheLargestWholeUnit(int hours, string expected)
    {
        Assert.Equal(expected, UnlockDelayText.Format(TimeSpan.FromHours(hours)));
    }

    [Theory]
    [InlineData(6, 6, "hours")]
    [InlineData(240, 10, "days")]
    [InlineData(336, 14, "days")]
    [InlineData(720, 1, "months")]
    [InlineData(18000, 25, "months")]
    public void Largest_SplitsIntoAmountAndUnitTheOwnerCanEdit(int hours, long amount, string unit)
    {
        var (gotAmount, gotUnit) = UnlockDelayText.Largest(TimeSpan.FromHours(hours));

        Assert.Equal(amount, gotAmount);
        Assert.Equal(unit, gotUnit);
    }
}
