using Foreword.Domain.Prayer;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>
/// How long a request has been held — the one fact on the prayer page the paper knows and the
/// owner would not. It is why the page is worth printing rather than remembering.
/// </summary>
public class HoldDurationTests
{
    private static readonly DateOnly Today = new(2026, 9, 6);

    [Fact]
    public void TheMorningItIsAddedItSaysSo()
    {
        Assert.Equal("added today", HoldDuration.Describe(Today, Today));
    }

    [Fact]
    public void OneDayIsSingular()
    {
        Assert.Equal("held 1 day", HoldDuration.Describe(Today.AddDays(-1), Today));
    }

    [Theory]
    [InlineData(4, "held 4 days")]
    [InlineData(9, "held 9 days")]
    [InlineData(13, "held 13 days")]
    public void UnderAFortnightItCountsDays(int days, string expected)
    {
        Assert.Equal(expected, HoldDuration.Describe(Today.AddDays(-days), Today));
    }

    /// <summary>
    /// Days stop being the useful unit somewhere around a fortnight: "held 43 days" is a number to
    /// work out, and "held 6 weeks" is a length of time.
    /// </summary>
    [Theory]
    [InlineData(14, "held 2 weeks")]
    [InlineData(21, "held 3 weeks")]
    [InlineData(44, "held 6 weeks")]
    [InlineData(83, "held 11 weeks")]
    public void PastAFortnightItCountsWeeks(int days, string expected)
    {
        Assert.Equal(expected, HoldDuration.Describe(Today.AddDays(-days), Today));
    }

    /// <summary>
    /// And weeks stop being useful too. This is past anything the mockup draws — nothing had been
    /// held that long when the page was designed — but the ladder has to answer for it, because a
    /// request that goes unanswered for a year is exactly the kind this page exists to keep.
    /// </summary>
    [Theory]
    [InlineData(90, "held 3 months")]
    [InlineData(400, "held 1 year")]
    [InlineData(800, "held 2 years")]
    public void PastAQuarterItCountsMonthsThenYears(int days, string expected)
    {
        Assert.Equal(expected, HoldDuration.Describe(Today.AddDays(-days), Today));
    }

    /// <summary>
    /// A held date in the future is a stored value that is wrong, not a request from tomorrow. It
    /// reads as new rather than as a negative count, because the page has no way to say "held -3
    /// days" that is better than saying nothing.
    /// </summary>
    [Fact]
    public void AHeldDateInTheFutureReadsAsNew()
    {
        Assert.Equal("added today", HoldDuration.Describe(Today.AddDays(3), Today));
    }
}
