using Constitution.Domain.Workouts.Screens;
using static Constitution.Tests.WorkoutsTab.WorkoutScreenFixture;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>C-5.4.5 (book p.12, D52): from a rest day, only the sessions that fit its gaps can be moved in; the rest are greyed. Invented data.</summary>
public class MoveHereTests
{
    [Fact]
    public void OffersOnlySessionsThatFit()
    {
        var card = MoveHere.For(Snapshot(), Fri);

        Assert.Equal("Move a workout to Friday", card.Title);
        Assert.Equal([Wed, Thu, Sat, Sun, Mon, NextTue], card.Rows.Select(row => row.Day));
        Assert.Equal([Wed, Sun, NextTue], card.Rows.Where(row => row.IsEnabled).Select(row => row.Day));
    }

    [Fact]
    public void AGreyedDayGivesItsReason()
    {
        var card = MoveHere.For(Snapshot(), Fri);

        Assert.Equal("two hard days in a row", card.Rows.Single(row => row.Day == Thu).Reason);
        Assert.Equal("Strength · 40 min", card.Rows.Single(row => row.Day == Thu).Text);
    }

    [Fact]
    public void AGapTooShortGreysTheSessionThatNeedsMore()
    {
        var card = MoveHere.For(Snapshot(OnlyFreeOn(Fri, (16, 0), (16, 30))), Fri);

        Assert.Equal("no 40-minute gap", card.Rows.Single(row => row.Day == Mon).Reason);
        Assert.True(card.Rows.Single(row => row.Day == Wed).IsEnabled);
    }

    [Fact]
    public void MovingASessionInLeavesItsOldDayRest()
    {
        var check = SwapChoices.Check(Snapshot(), Wed, Fri);

        Assert.True(check.IsAllowed);
        var move = Assert.Single(check.Result!.Moves);
        Assert.Equal(Fri, move.New.Day);
        Assert.Empty(check.Result.After[0].Plan.PlanFor("Sam")!.SessionsOn(Wed));
    }
}
