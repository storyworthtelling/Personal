using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>C-2.3.6 — a delivered document is replaced only before its slot, and never once the session has begun.</summary>
public class RedeliveryTests
{
    private static readonly DateTimeOffset Slot = Est(11, 3, 18);
    private static readonly DateTimeOffset SlotEnd = Est(11, 3, 18, 40);

    private static DeliveredWorkout Delivered(DateTimeOffset? slot = null, string fingerprint = "aaa") =>
        new("Constitution — Workout — 2026-11-03 — Sam", Sam, new DateOnly(2026, 11, 3), SessionKind.OutdoorCardio, slot ?? Slot, fingerprint, false);

    private static CurrentWorkout Current(string fingerprint = "bbb", DateTimeOffset? slot = null, DateTimeOffset? end = null) =>
        new(fingerprint, slot ?? Slot, end ?? SlotEnd);

    [Fact]
    public void ReplacesBeforeTheSlot()
    {
        // A flipped forecast changed the page; the owner has until six.
        Assert.Equal(Redelivery.Replace, RedeliveryRules.Decide(Delivered(), Current(), Est(11, 3, 12)));
    }

    [Fact]
    public void NeverReplacesOnceStarted()
    {
        Assert.Equal(Redelivery.TooLate, RedeliveryRules.Decide(Delivered(), Current(), Est(11, 3, 18, 10)));
    }

    [Fact]
    public void TheMomentTheSlotStartsHasBegun()
    {
        Assert.Equal(Redelivery.TooLate, RedeliveryRules.Decide(Delivered(), Current(), Slot));
    }

    [Fact]
    public void OneMinuteBeforeTheSlotStillReplaces()
    {
        Assert.Equal(Redelivery.Replace, RedeliveryRules.Decide(Delivered(), Current(), Slot.AddMinutes(-1)));
    }

    [Fact]
    public void NeverReplacesAfterTheSessionIsOver()
    {
        Assert.Equal(Redelivery.TooLate, RedeliveryRules.Decide(Delivered(), Current(), Est(11, 3, 20)));
    }

    [Fact]
    public void AnUnchangedPageIsLeftAloneBeforeTheSlot()
    {
        Assert.Equal(Redelivery.LeaveAlone, RedeliveryRules.Decide(Delivered(), Current("aaa"), Est(11, 3, 12)));
    }

    [Fact]
    public void AnUnchangedPageIsLeftAloneAfterTheSlotToo()
    {
        Assert.Equal(Redelivery.LeaveAlone, RedeliveryRules.Decide(Delivered(), Current("aaa"), Est(11, 3, 19)));
    }

    [Fact]
    public void MovedLaterJudgedByTheEarlierStart()
    {
        // The paper on the tablet says six; the plan now says seven. At 6:10 the owner may already have started from the old paper.
        var moved = Current(slot: Est(11, 3, 19), end: Est(11, 3, 19, 40));

        Assert.Equal(Redelivery.TooLate, RedeliveryRules.Decide(Delivered(), moved, Est(11, 3, 18, 10)));
        Assert.Equal(Redelivery.Replace, RedeliveryRules.Decide(Delivered(), moved, Est(11, 3, 17, 59)));
    }

    [Fact]
    public void MovedEarlierJudgedByTheEarlierStart()
    {
        var moved = Current(slot: Est(11, 3, 17), end: Est(11, 3, 17, 40));

        Assert.Equal(Redelivery.TooLate, RedeliveryRules.Decide(Delivered(), moved, Est(11, 3, 17, 5)));
        Assert.Equal(Redelivery.Replace, RedeliveryRules.Decide(Delivered(), moved, Est(11, 3, 16, 59)));
    }

    [Fact]
    public void ASessionCalledOffBeforeItsSlotIsWithdrawn()
    {
        Assert.Equal(Redelivery.Withdraw, RedeliveryRules.Decide(Delivered(), null, Est(11, 3, 12)));
    }

    [Fact]
    public void ACalledOffBegunSessionIsLeft()
    {
        Assert.Equal(Redelivery.TooLate, RedeliveryRules.Decide(Delivered(), null, Est(11, 3, 18, 5)));
    }

    [Fact]
    public void NothingPlannedAndNothingDeliveredIsNothing()
    {
        Assert.Equal(Redelivery.Nothing, RedeliveryRules.Decide(null, null, Est(11, 3, 12)));
    }

    [Fact]
    public void AFirstSendBeforeTheSlotSends()
    {
        Assert.Equal(Redelivery.Send, RedeliveryRules.Decide(null, Current(), Est(11, 3, 6)));
    }

    [Fact]
    public void AFirstSendStillGoesUntilTheEnd()
    {
        // A page the owner never had cannot change under them; a run that came late is not a reason to leave them without paper.
        Assert.Equal(Redelivery.Send, RedeliveryRules.Decide(null, Current(), Est(11, 3, 18, 15)));
    }

    [Fact]
    public void AFirstSendAfterTheSessionIsOverIsTooLate()
    {
        Assert.Equal(Redelivery.TooLate, RedeliveryRules.Decide(null, Current(), SlotEnd));
    }

    [Fact]
    public void AnyOtherFingerprintIsAChange()
    {
        Assert.Equal(Redelivery.Replace, RedeliveryRules.Decide(Delivered(fingerprint: ""), Current("x"), Est(11, 3, 9)));
    }
}
