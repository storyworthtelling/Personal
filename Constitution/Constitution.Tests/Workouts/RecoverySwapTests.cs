using Constitution.Domain.Body;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>
/// C-5.2.4 · Recovery, the plan half. Under-recovery signals by the morning turn that day's hard session into mobility,
/// for the card and Today's row; a recovered, unread or unjudgeable morning leaves the plan alone. A recovery that is
/// short at planning time eases the week through the planner's own existing input.
///
/// <para>The reads here are built by hand. Reading Health Connect itself is <see cref="RecoveryReaderTests"/>'s half,
/// and the half no test can reach: a phone delivering a morning HRV.</para>
/// </summary>
public class RecoverySwapTests
{
    private static readonly DateOnly Today = Day(2);

    /// <summary>Wednesday 4 November, 06:00: before every session the tests place that day.</summary>
    private static readonly DateTimeOffset EarlyMorning = At(2, 6);

    private static RecoveryRead Under(params RecoveryReason[] reasons) =>
        new(RecoveryState.UnderRecovered, reasons.Length == 0 ? [RecoveryReason.ShortSleep] : reasons, [], []);

    private static readonly RecoveryRead Fine = new(RecoveryState.Recovered, [], [], []);

    private static IReadOnlyDictionary<string, RecoveryRead> Reads(params (string Person, RecoveryRead Read)[] reads) =>
        reads.ToDictionary(item => item.Person, item => item.Read, StringComparer.OrdinalIgnoreCase);

    private static RecoveryAdjusted Swap(HouseholdWorkoutPlan plan, IReadOnlyDictionary<string, RecoveryRead> reads, DateTimeOffset? now = null, EquipmentSet? equipment = null) =>
        RecoverySwap.Apply(plan, reads, Today, now ?? EarlyMorning, equipment ?? EquipmentSet.Everything());

    // ---- UnderRecoveredBecomesMobility ----

    [Fact]
    public void UnderRecoveredBecomesMobility()
    {
        var plan = HandmadePlans.Week(
            [HandmadePlans.Placed(2, SessionKind.Strength, [Sam]), HandmadePlans.Placed(4, SessionKind.Strength, [Sam])],
            [HandmadePlans.Placed(2, SessionKind.Strength, [Lee])]);

        var result = Swap(plan, Reads((Sam, Under(RecoveryReason.ShortSleep))));

        var before = HandmadePlans.OnDay(plan, Sam, 2);
        var swapped = HandmadePlans.OnDay(result.Plan, Sam, 2);
        Assert.Equal(SessionKind.Mobility, swapped.Session.Kind);
        Assert.False(swapped.Session.IsSweaty);
        Assert.Equal(TimeSpan.FromMinutes(20), swapped.Session.Duration);
        Assert.Equal(before.Start, swapped.Start);
        Assert.Equal(before.Gap, swapped.Gap);
        Assert.Equal(before.Day, swapped.Day);
        Assert.Equal(before.Participants, swapped.Participants);
        Assert.Equal(Together.None, swapped.Together);

        var note = Assert.Single(result.Swaps);
        Assert.Equal([Sam], note.People.ToArray());
        Assert.Equal(Day(2), note.Day);
        Assert.Equal(SessionKind.Strength, note.Was);
        Assert.Equal("Mobility today: short sleep", note.Reason);
    }

    [Theory]
    [InlineData(SessionKind.Strength)]
    [InlineData(SessionKind.OutdoorCardio)]
    [InlineData(SessionKind.Treadmill)]
    public void EveryHardKindBecomesMobility(SessionKind kind)
    {
        var plan = HandmadePlans.Week([HandmadePlans.Placed(2, kind, [Sam])]);

        var result = Swap(plan, Reads((Sam, Under())));

        Assert.Equal(SessionKind.Mobility, HandmadePlans.OnDay(result.Plan, Sam, 2).Session.Kind);
        Assert.Equal(kind, Assert.Single(result.Swaps).Was);
    }

    [Theory]
    [InlineData(SessionKind.Mobility)]
    [InlineData(SessionKind.GentleWalk)]
    public void AnEasySessionIsLeftAlone(SessionKind kind)
    {
        var plan = HandmadePlans.Week([HandmadePlans.Placed(2, kind, [Sam])]);

        var result = Swap(plan, Reads((Sam, Under())));

        Assert.Same(plan, result.Plan);
        Assert.Empty(result.Swaps);
    }

    [Fact]
    public void OnlyThisMorningsSessionIsSwapped()
    {
        var plan = HandmadePlans.Week(
        [
            HandmadePlans.Placed(1, SessionKind.Strength, [Sam]),
            HandmadePlans.Placed(2, SessionKind.Strength, [Sam]),
            HandmadePlans.Placed(3, SessionKind.OutdoorCardio, [Sam]),
        ]);

        var result = Swap(plan, Reads((Sam, Under())));

        Assert.Equal(HandmadePlans.OnDay(plan, Sam, 1), HandmadePlans.OnDay(result.Plan, Sam, 1));
        Assert.Equal(SessionKind.Mobility, HandmadePlans.OnDay(result.Plan, Sam, 2).Session.Kind);
        Assert.Equal(HandmadePlans.OnDay(plan, Sam, 3), HandmadePlans.OnDay(result.Plan, Sam, 3));
    }

    [Fact]
    public void OnlyThePersonWhoIsUnderRecoveredIsSwapped()
    {
        var plan = HandmadePlans.Week([HandmadePlans.Placed(2, SessionKind.Strength, [Sam])], [HandmadePlans.Placed(2, SessionKind.Strength, [Lee])]);

        var result = Swap(plan, Reads((Sam, Under()), (Lee, Fine)));

        Assert.Equal(SessionKind.Mobility, HandmadePlans.OnDay(result.Plan, Sam, 2).Session.Kind);
        Assert.Equal(HandmadePlans.OnDay(plan, Lee, 2), HandmadePlans.OnDay(result.Plan, Lee, 2));
    }

    [Fact]
    public void ARecoveredMorningLeavesThePlanExactlyAsPlanned()
    {
        var plan = Plan(Calendars());

        var result = Swap(plan, Reads((Sam, Fine), (Lee, Fine)));

        Assert.Same(plan, result.Plan);
        Assert.Empty(result.Swaps);
    }

    [Fact]
    public void NotEnoughToSayLeavesThePlanExactlyAsPlanned()
    {
        var plan = HandmadePlans.Week([HandmadePlans.Placed(2, SessionKind.Strength, [Sam])]);

        var result = Swap(plan, Reads((Sam, RecoveryRead.Nothing)));

        Assert.Same(plan, result.Plan);
        Assert.Empty(result.Swaps);
    }

    [Fact]
    public void NobodyReadLeavesThePlanExactlyAsPlanned()
    {
        var plan = HandmadePlans.Week([HandmadePlans.Placed(2, SessionKind.Strength, [Sam])]);

        var result = Swap(plan, Reads());

        Assert.Same(plan, result.Plan);
        Assert.Empty(result.Swaps);
    }

    [Fact]
    public void AnAlarmingMorningSwapsToo()
    {
        var plan = HandmadePlans.Week([HandmadePlans.Placed(2, SessionKind.Strength, [Sam])]);
        var alarming = new RecoveryRead(RecoveryState.Alarming, [RecoveryReason.ShortSleep, RecoveryReason.RaisedRestingHeartRate], [], []);

        var result = Swap(plan, Reads((Sam, alarming)));

        Assert.Equal(SessionKind.Mobility, HandmadePlans.OnDay(result.Plan, Sam, 2).Session.Kind);
        Assert.Equal("Mobility today: short sleep and raised resting heart rate", Assert.Single(result.Swaps).Reason);
    }

    // ---- a session that has begun ----

    [Fact]
    public void ASessionThatHasBegunIsNeverReplaced()
    {
        var plan = HandmadePlans.Week([HandmadePlans.Placed(2, SessionKind.Strength, [Sam], hour: 17)]);

        var atTheStart = Swap(plan, Reads((Sam, Under())), now: At(2, 17));
        var after = Swap(plan, Reads((Sam, Under())), now: At(2, 17, 20));
        var justBefore = Swap(plan, Reads((Sam, Under())), now: At(2, 16, 59));

        Assert.Same(plan, atTheStart.Plan);
        Assert.Same(plan, after.Plan);
        Assert.Empty(after.Swaps);
        Assert.Equal(SessionKind.Mobility, HandmadePlans.OnDay(justBefore.Plan, Sam, 2).Session.Kind);
    }

    // ---- joint sessions ----

    [Fact]
    public void ASharedSessionBecomesMobilityForBoth()
    {
        var joint = HandmadePlans.Placed(2, SessionKind.Strength, [Sam, Lee], Together.AfterBedtime, hour: 20);
        var plan = HandmadePlans.Week([], [], [joint]);

        var result = Swap(plan, Reads((Sam, Under(RecoveryReason.LowHeartRateVariability)), (Lee, Fine)));

        var shared = Assert.Single(result.Plan.JointSessions);
        Assert.Equal(SessionKind.Mobility, shared.Session.Kind);
        Assert.Equal(Together.AfterBedtime, shared.Together);
        Assert.Equal([Sam, Lee], shared.Participants.ToArray());
        Assert.Same(shared, HandmadePlans.OnDay(result.Plan, Sam, 2));
        Assert.Same(shared, HandmadePlans.OnDay(result.Plan, Lee, 2));
        var note = Assert.Single(result.Swaps);
        Assert.Equal([Sam, Lee], note.People.ToArray());
        Assert.Equal("Mobility today: low heart rate variability", note.Reason);
    }

    [Fact]
    public void WhenBothAreUnderTheReasonNamesEachOnce()
    {
        var joint = HandmadePlans.Placed(2, SessionKind.Strength, [Sam, Lee], Together.AfterBedtime, hour: 20);

        var result = Swap(
            HandmadePlans.Week([], [], [joint]),
            Reads((Sam, Under(RecoveryReason.ShortSleep)), (Lee, Under(RecoveryReason.ShortSleep, RecoveryReason.RaisedRestingHeartRate))));

        Assert.Equal("Mobility today: short sleep and raised resting heart rate", Assert.Single(result.Swaps).Reason);
    }

    // ---- reason lines ----

    [Theory]
    [InlineData(new[] { RecoveryReason.LowHeartRateVariability }, "Mobility today: low heart rate variability")]
    [InlineData(new[] { RecoveryReason.ShortSleep }, "Mobility today: short sleep")]
    [InlineData(new[] { RecoveryReason.RaisedRestingHeartRate }, "Mobility today: raised resting heart rate")]
    [InlineData(new[] { RecoveryReason.ShortSleep, RecoveryReason.LowHeartRateVariability }, "Mobility today: low heart rate variability and short sleep")]
    [InlineData(new[] { RecoveryReason.RaisedRestingHeartRate, RecoveryReason.ShortSleep, RecoveryReason.LowHeartRateVariability }, "Mobility today: low heart rate variability, short sleep and raised resting heart rate")]
    public void TheReasonLineNamesTheSignalsInWordsOnly(RecoveryReason[] reasons, string expected)
    {
        var plan = HandmadePlans.Week([HandmadePlans.Placed(2, SessionKind.Strength, [Sam])]);

        var result = Swap(plan, Reads((Sam, Under(reasons))));

        var line = Assert.Single(result.Swaps).Reason;
        Assert.Equal(expected, line);
        Assert.DoesNotContain(line, char.IsDigit);
    }

    [Fact]
    public void StaleValuesStillSwapAndTheReadKeepsTheNote()
    {
        var stale = new RecoveryRead(RecoveryState.UnderRecovered, [RecoveryReason.RaisedRestingHeartRate], [BodySignal.RestingHeartRate], []);
        var plan = HandmadePlans.Week([HandmadePlans.Placed(2, SessionKind.Strength, [Sam])]);

        var result = Swap(plan, Reads((Sam, stale)));

        Assert.Equal(SessionKind.Mobility, HandmadePlans.OnDay(result.Plan, Sam, 2).Session.Kind);
        Assert.True(stale.UsedStaleValues);
    }

    // ---- the equipment ----

    [Fact]
    public void TheMobilityNeverNeedsAnUntickedItem()
    {
        var plan = HandmadePlans.Week([HandmadePlans.Placed(2, SessionKind.Strength, [Sam])]);

        var none = HandmadePlans.OnDay(Swap(plan, Reads((Sam, Under())), equipment: EquipmentSet.Of([])).Plan, Sam, 2).Session;
        var mat = HandmadePlans.OnDay(Swap(plan, Reads((Sam, Under())), equipment: EquipmentSet.Of([EquipmentCatalogue.MatId])).Plan, Sam, 2).Session;

        Assert.Equal(SessionKind.Mobility, none.Kind);
        Assert.Equal(SessionKind.Mobility, mat.Kind);
        Assert.Empty(none.EquipmentNeeded);
        Assert.NotEmpty(mat.Exercises);
        Assert.Contains(EquipmentCatalogue.MatId, mat.EquipmentNeeded);
        Assert.All(mat.EquipmentNeeded, id => Assert.Equal(EquipmentCatalogue.MatId, id));
    }

    [Fact]
    public void ARealPlansSwapKeepsEveryPlannerPromise()
    {
        var request = Request(Calendars());
        var plan = WorkoutPlanner.Plan(request);
        var firstHard = plan.PlanFor(Sam)!.Sessions.First(item => SessionKinds.IsHard(item.Session.Kind));

        var result = RecoverySwap.Apply(plan, Reads((Sam, Under())), firstHard.Day, firstHard.Start.AddHours(-1), request.Equipment);

        var swapped = result.Plan.PlanFor(Sam)!.Sessions.Single(item => item.Day == firstHard.Day);
        Assert.Equal(SessionKind.Mobility, swapped.Session.Kind);
        Assert.True(swapped.Start >= swapped.Gap.Start && swapped.End <= swapped.Gap.End);
        Assert.Equal(firstHard.Start, swapped.Start);
        Assert.Equal(plan.PlanFor(Sam)!.Sessions.Count, result.Plan.PlanFor(Sam)!.Sessions.Count);
        Assert.Equal(plan.PlanFor(Lee)!.Sessions, result.Plan.PlanFor(Lee)!.Sessions);
    }

    // ---- Ease: the planner's own input ----

    [Fact]
    public void ShortRecoveryEasesTheWeekThroughTheExistingInput()
    {
        var earned = new RecoveryInputs { SessionsDoneLastWeek = 3, SessionsSkippedLastWeek = 0, LastHardSessionDay = Day(-2) };
        Assert.Equal(WorkoutFrequency.Earned, WorkoutFrequency.Target(earned));

        var eased = RecoverySwap.Ease(earned, Under());

        Assert.Equal(WorkoutFrequency.Eased, WorkoutFrequency.Target(eased));
        Assert.Equal(earned.SessionsDoneLastWeek, eased.SessionsDoneLastWeek);
        Assert.Equal(earned.LastHardSessionDay, eased.LastHardSessionDay);
    }

    [Fact]
    public void AnEasedWeekHoldsNoMoreThanTwoEffortSessions()
    {
        var earned = new RecoveryInputs { SessionsDoneLastWeek = 4, SessionsSkippedLastWeek = 0 };

        var easedPlan = Plan(Calendars(), bothAdults: false, sam: RecoverySwap.Ease(earned, Under()));
        var normalPlan = Plan(Calendars(), bothAdults: false, sam: earned);

        int Count(HouseholdWorkoutPlan plan) => plan.PlanFor(Sam)!.Sessions.Count(item => item.Session.Kind != SessionKind.GentleWalk);
        Assert.True(Count(normalPlan) > 2, "The unadjusted week was already two sessions or fewer, so the test proves nothing.");
        Assert.True(Count(easedPlan) <= WorkoutFrequency.Eased);
    }

    [Theory]
    [InlineData(RecoveryState.Recovered)]
    [InlineData(RecoveryState.NotEnoughToSay)]
    public void AGoodOrUnknownMorningDoesNotEaseTheWeek(RecoveryState state)
    {
        var earned = new RecoveryInputs { SessionsDoneLastWeek = 3 };

        var same = RecoverySwap.Ease(earned, new RecoveryRead(state, [], [], []));

        Assert.Equal(earned, same);
    }

    [Theory]
    [InlineData(RecoveryState.UnderRecovered)]
    [InlineData(RecoveryState.Alarming)]
    public void ShortAndAlarmingBothEase(RecoveryState state)
    {
        var eased = RecoverySwap.Ease(RecoveryInputs.Fresh, new RecoveryRead(state, [RecoveryReason.ShortSleep], [], []));

        Assert.Equal(WorkoutFrequency.Eased, WorkoutFrequency.Target(eased));
    }

    [Fact]
    public void AWeekAlreadyEasedByMoreSkipsIsNotMadeWorse()
    {
        var skipped = new RecoveryInputs { SessionsSkippedLastWeek = 4 };

        var eased = RecoverySwap.Ease(skipped, Under());

        Assert.Equal(4, eased.SessionsSkippedLastWeek);
    }

    [Fact]
    public void EasingNeverChangesWhatItWasHanded()
    {
        var basis = new RecoveryInputs { SessionsDoneLastWeek = 3 };

        _ = RecoverySwap.Ease(basis, Under());

        Assert.Equal(0, basis.SessionsSkippedLastWeek);
    }

    // ---- refusals ----

    [Fact]
    public void ThePlanAndTheReadsAreRequired()
    {
        var plan = HandmadePlans.Week([]);

        Assert.Throws<ArgumentNullException>(() => RecoverySwap.Apply(null!, Reads(), Today, EarlyMorning, EquipmentSet.Everything()));
        Assert.Throws<ArgumentNullException>(() => RecoverySwap.Apply(plan, null!, Today, EarlyMorning, EquipmentSet.Everything()));
        Assert.Throws<ArgumentNullException>(() => RecoverySwap.Apply(plan, Reads(), Today, EarlyMorning, null!));
        Assert.Throws<ArgumentNullException>(() => RecoverySwap.Ease(null!, Fine));
        Assert.Throws<ArgumentNullException>(() => RecoverySwap.Ease(RecoveryInputs.Fresh, null!));
    }
}
