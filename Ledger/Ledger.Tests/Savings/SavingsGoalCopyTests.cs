using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Savings;

/// <summary>
/// Remediation 2.5 item 4: a goal that is already met says so. <see cref="SavingsPace.ReachesIt"/> returns
/// THIS month for a met goal (Guard G23 keeps that), which the card printed as "Around Oct 2026 at this
/// pace" and the post-it as "Oct 2026" — a projection of something already true, and not drawn in
/// mockups 9 or 16. G23's "not at this pace" rule is untouched: it still applies to an unmet goal.
/// </summary>
public class SavingsGoalCopyTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 2);

    private static SavingsReachesIt Reaches(decimal balance, decimal goal, decimal pace) =>
        SavingsPace.ReachesIt(balance, goal, pace, AsOf);

    [Theory]
    [InlineData(15_000, 15_000, 260)]
    [InlineData(20_000, 15_000, 260)]
    [InlineData(15_000, 15_000, 0)]
    [InlineData(20_000, 15_000, -40)]
    public void AMetGoalSaysGoalReachedOnTheCard_WhateverThePace(int balance, int goal, int pace)
    {
        var text = SavingsGoalCopy.Card(balance, goal, Reaches(balance, goal, pace));

        Assert.Equal("Goal reached", text);
    }

    [Theory]
    [InlineData(15_000, 15_000, 260)]
    [InlineData(20_000, 15_000, 0)]
    public void AMetGoalSaysGoalReachedInThePostIt_WhateverThePace(int balance, int goal, int pace)
    {
        var text = SavingsGoalCopy.PostIt(balance, goal, Reaches(balance, goal, pace));

        Assert.Equal("Goal reached", text);
    }

    [Fact]
    public void AnUnmetGoalStillGetsItsMonth()
    {
        var reaches = Reaches(8_200m, 15_000m, 260m);

        Assert.Equal($"Around {AsOf.AddMonths(27):MMM yyyy} at this pace", SavingsGoalCopy.Card(8_200m, 15_000m, reaches));
        Assert.Equal($"{AsOf.AddMonths(27):MMM yyyy}", SavingsGoalCopy.PostIt(8_200m, 15_000m, reaches));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-40)]
    public void AnUnmetGoalWithAFlatOrFallingPaceStillSaysNotAtThisPace(int pace)
    {
        var reaches = Reaches(1_350m, 5_000m, pace);

        Assert.Equal("Not at this pace", SavingsGoalCopy.Card(1_350m, 5_000m, reaches));
        Assert.Equal("not at this pace", SavingsGoalCopy.PostIt(1_350m, 5_000m, reaches));
    }

    [Fact]
    public void OneCentShortIsNotReached()
    {
        Assert.False(SavingsGoalCopy.IsReached(14_999.99m, 15_000m));
        Assert.True(SavingsGoalCopy.IsReached(15_000m, 15_000m));
    }
}
