using Foreword.Domain.Tasks;
using Xunit;

namespace Foreword.Tests.Tasks;

/// <summary>
/// The away pause (A1). Two dates and three toggles — the one thing the owner asked for that Google
/// Tasks cannot express.
/// </summary>
public class AwayPauseTests
{
    private static readonly AwayPause October = new()
    {
        From = new DateOnly(2026, 10, 12),
        Until = new DateOnly(2026, 10, 19),
    };

    /// <summary>
    /// Inclusive at both ends: the last morning away is still a morning away. An exclusive end would
    /// print the bins on the day of the flight home, which is the one morning of the trip that
    /// definitely does not want them.
    /// </summary>
    [Fact]
    public void BothEndsAreInside()
    {
        Assert.True(October.Covers(new DateOnly(2026, 10, 12)));
        Assert.True(October.Covers(new DateOnly(2026, 10, 19)));
        Assert.True(October.Covers(new DateOnly(2026, 10, 15)));
    }

    [Fact]
    public void TheDaysEitherSideAreOrdinary()
    {
        Assert.False(October.Covers(new DateOnly(2026, 10, 11)));
        Assert.False(October.Covers(new DateOnly(2026, 10, 20)));
    }

    /// <summary>Twelve to nineteen October is eight mornings, which is what the screen says.</summary>
    [Fact]
    public void TheCountIsMorningsRatherThanNights()
    {
        Assert.Equal(8, October.Mornings);
    }

    [Fact]
    public void NoTripCoversNothing()
    {
        Assert.False(AwayPause.None.Covers(new DateOnly(2026, 10, 15)));
        Assert.Equal(0, AwayPause.None.Mornings);
    }

    /// <summary>
    /// Half a range is not a range. A From with no Until is a trip being typed in, and pausing
    /// everything from the moment the first date is entered would silently stop the mornings.
    /// </summary>
    [Fact]
    public void HalfADateRangePausesNothing()
    {
        var half = new AwayPause { From = new DateOnly(2026, 10, 12) };

        Assert.False(half.Covers(new DateOnly(2026, 10, 15)));
        Assert.Equal(0, half.Mornings);
    }

    /// <summary>
    /// The morning still goes out by default. A holiday is exactly when a day sheet earns its place,
    /// and a default that stopped delivery would make "away" mean "off" — a different setting.
    /// </summary>
    [Fact]
    public void TheMorningStillGoesOutUnlessItIsTurnedOff()
    {
        Assert.True(AwayPause.None.StillDeliver);
        Assert.True(October.StillDeliver);
    }
}
