using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Screens;
using Constitution.Tests.Placement;
using Constitution.Tests.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.WorkoutsTab.WorkoutScreenFixture;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>C-5.4.3 (book p.12, D51): swap only with a day that has room, and say why the others do not. Invented data.</summary>
public class SwapTests
{
    private static ChoiceRow Row(ChoiceCard card, DateOnly day) => card.Rows.Single(item => item.Day == day);

    [Fact]
    public void TheCardListsTheOtherSixDaysAndHidesNone()
    {
        var card = SwapChoices.For(Snapshot(), Thu);

        Assert.Equal("Swap Thursday’s strength with…", card.Title);
        Assert.Equal("Both move; the calendar follows", card.Subtitle);
        Assert.Equal([Wed, Fri, Sat, Sun, Mon, NextTue], card.Rows.Select(row => row.Day));
        Assert.Equal("Mobility · 20 min", Row(card, Wed).Text);
        Assert.Equal("Rest", Row(card, Fri).Text);
        Assert.Equal("Treadmill · 30 min", Row(card, Sat).Text);
    }

    [Fact]
    public void ADayWithoutRoomIsGreyedWithItsReason()
    {
        var card = SwapChoices.For(Snapshot(OnlyFreeOn(Sat, (8, 0), (8, 30))), Thu);

        var sat = Row(card, Sat);
        Assert.False(sat.IsEnabled);
        Assert.Equal("no 40-minute gap", sat.Reason);
        Assert.Contains(sat, card.Rows);
    }

    [Fact]
    public void ADayThatFitsCanBePicked()
    {
        Assert.True(Row(SwapChoices.For(Snapshot(), Thu), Mon).IsEnabled);
    }

    [Fact]
    public void SundaysStayGentle()
    {
        Assert.Equal("Sundays are gentle walks", Row(SwapChoices.For(Snapshot(), Thu), Sun).Reason);
    }

    [Fact]
    public void TwoHardDaysInARowAreRefused()
    {
        var card = SwapChoices.For(Snapshot(), Thu);

        Assert.Equal("two hard days in a row", Row(card, Wed).Reason);
        Assert.Equal("two hard days in a row", Row(card, Fri).Reason);
    }

    [Fact]
    public void ASharedSessionStaysWithEveryone()
    {
        var card = SwapChoices.For(Snapshot(sundayTogether: Together.FamilyWalk), Wed);

        Assert.Equal("shared with everyone", Row(card, Sun).Reason);
    }

    [Fact]
    public void ARuleBrokenBeforeTheSwapDoesNotGreyADay()
    {
        // No bench is ticked, so every strength session already breaks the equipment rule. That is not the swap's doing.
        var snapshot = Snapshot(equipment: Constitution.Domain.Equipment.EquipmentSet.Everything().With(Constitution.Domain.Equipment.EquipmentCatalogue.BenchId, false));

        Assert.True(Row(SwapChoices.For(snapshot, Thu), Mon).IsEnabled);
    }

    [Fact]
    public void ADaySwappedIntoPlaceTakesTheOtherSessionsTime()
    {
        var check = SwapChoices.Check(Snapshot(), Thu, Sat);

        Assert.True(check.IsAllowed);
        var moves = check.Result!.Moves;
        Assert.Equal(2, moves.Count);
        var strength = moves.Single(move => move.Old.Session.Kind == SessionKind.Strength);
        var treadmill = moves.Single(move => move.Old.Session.Kind == SessionKind.Treadmill);
        Assert.Equal(Sat, strength.New.Day);
        Assert.Equal(Thu, treadmill.New.Day);
        Assert.InRange(strength.New.Start, strength.New.Gap.Start, strength.New.Gap.End - strength.New.Session.Duration);
        Assert.Empty(PlanRules.Violations(check.Result.After[0].Request, check.Result.After[0].Plan).Except(PlanRules.Violations(check.Result.Before[0].Request, check.Result.Before[0].Plan)));
    }

    [Fact]
    public void ARefusedSwapGivesNoResult()
    {
        var check = SwapChoices.Check(Snapshot(), Thu, Wed);

        Assert.False(check.IsAllowed);
        Assert.Equal("two hard days in a row", check.Reason);
    }

    [Fact]
    public void ASwapAppliedMovesTheSessionsAndTheirFacts()
    {
        var snapshot = Snapshot();
        var swap = SwapChoices.Check(snapshot, Thu, Sat).Result!;

        var next = SwapChoices.Apply(snapshot, swap);

        Assert.Equal(SessionKind.Strength, next.SessionOn(Sat)!.Session.Kind);
        Assert.Equal(SessionKind.Treadmill, next.SessionOn(Thu)!.Session.Kind);
        Assert.Equal(132, next.FactsOn(Sat).ScoreBpm);
        Assert.Equal(140, next.FactsOn(Thu).ScoreBpm);
        Assert.Null(next.FactsOn(Thu).ChangeReason);
        Assert.Equal(next.SessionOn(Sat)!.Start, next.FactsOn(Sat).OnCalendar!.Start);
        Assert.Equal(snapshot.Weeks[1], next.Weeks[1]);
    }

    [Fact]
    public void AMoveIntoARestDayLeavesTheOldDayBare()
    {
        var snapshot = Snapshot();
        var next = SwapChoices.Apply(snapshot, SwapChoices.Check(snapshot, Wed, Fri).Result!);

        Assert.Null(next.SessionOn(Wed));
        Assert.Null(next.FactsOn(Wed).ScoreBpm);
        Assert.Equal(118, next.FactsOn(Fri).ScoreBpm);
        Assert.Equal(SessionKind.Mobility, next.SessionOn(Fri)!.Session.Kind);
    }

    // ---- the calendar follows ----

    private static (FakeHeldCalendar Calendar, MemoryKeyStore Store, WorkoutCalendarFollow Follow, string Strength, string Treadmill) Seeded()
    {
        var calendar = new FakeHeldCalendar();
        var store = new MemoryKeyStore();
        var week = PlacementKey.WeekIndexOf(Tue);
        var strengthKey = PlacementKey.ForWorkout(Sam, SessionKind.Strength, week, 1);
        var treadmillKey = PlacementKey.ForWorkout(Sam, SessionKind.Treadmill, week, 0);
        var strength = calendar.Add(Sam, strengthKey, Est(11, 5, 16, 0), Est(11, 5, 16, 40));
        var treadmill = calendar.Add(Sam, treadmillKey, Est(11, 7, 8, 0), Est(11, 7, 8, 30));
        store.Save(WrittenKeys.Empty.With([strengthKey, treadmillKey]));
        return (calendar, store, new WorkoutCalendarFollow(calendar, calendar, store), strength, treadmill);
    }

    [Fact]
    public async Task BothSessionsAndEventsMove()
    {
        var (calendar, _, follow, strength, treadmill) = Seeded();
        var swap = SwapChoices.Check(Snapshot(), Thu, Sat).Result!;

        var outcomes = await follow.FollowSwapAsync(swap, CancellationToken.None);

        Assert.Equal([FollowOutcome.Moved, FollowOutcome.Moved], outcomes);
        var strengthNew = swap.Moves.Single(move => move.Old.Session.Kind == SessionKind.Strength).New;
        var treadmillNew = swap.Moves.Single(move => move.Old.Session.Kind == SessionKind.Treadmill).New;
        Assert.Equal(strengthNew.Start, calendar.ById(strength).Start);
        Assert.Equal(strengthNew.End, calendar.ById(strength).End);
        Assert.Equal(treadmillNew.Start, calendar.ById(treadmill).Start);
        Assert.Equal(treadmillNew.End, calendar.ById(treadmill).End);
    }

    [Fact]
    public async Task AnEventTheOwnerMovedIsLeftWhereTheyPutIt()
    {
        var (calendar, _, follow, strength, treadmill) = Seeded();
        calendar.OwnerMoves(strength, Est(11, 5, 19, 0), Est(11, 5, 19, 40));
        var swap = SwapChoices.Check(Snapshot(), Thu, Sat).Result!;

        var outcomes = await follow.FollowSwapAsync(swap, CancellationToken.None);

        Assert.Contains(FollowOutcome.OwnerHadMovedIt, outcomes);
        Assert.Equal(Est(11, 5, 19, 0), calendar.ById(strength).Start);
        Assert.Equal(Est(11, 5, 19, 40), calendar.ById(strength).End);
        Assert.NotEqual(Est(11, 7, 8, 0), calendar.ById(treadmill).Start);
    }

    [Fact]
    public async Task ACalendarThatCannotBeLookedIntoIsNotTouched()
    {
        var (calendar, _, follow, _, _) = Seeded();
        calendar.Unavailable = true;
        var swap = SwapChoices.Check(Snapshot(), Thu, Sat).Result!;

        var outcomes = await follow.FollowSwapAsync(swap, CancellationToken.None);

        Assert.All(outcomes, outcome => Assert.Equal(FollowOutcome.Unavailable, outcome));
        Assert.Equal(0, calendar.Edits);
    }

    [Fact]
    public async Task AnUnreadableRecordBlocksTheEdit()
    {
        var (calendar, store, follow, _, _) = Seeded();
        store.Json = "not a record";
        var swap = SwapChoices.Check(Snapshot(), Thu, Sat).Result!;

        var outcomes = await follow.FollowSwapAsync(swap, CancellationToken.None);

        Assert.All(outcomes, outcome => Assert.Equal(FollowOutcome.Unavailable, outcome));
        Assert.Equal(0, calendar.Edits);
        Assert.Equal("not a record", store.Json);
    }

    [Fact]
    public async Task AMovedEventWearsTheKeyOfItsNewPlaceAndIsRecordedAsWritten()
    {
        var calendar = new FakeHeldCalendar();
        var store = new MemoryKeyStore();
        var oldKey = PlacementKey.ForWorkout(Sam, SessionKind.Mobility, PlacementKey.WeekIndexOf(Wed), 0);
        var otherKey = PlacementKey.ForWorkout(Sam, SessionKind.Mobility, PlacementKey.WeekIndexOf(NextTue), 0);
        var wed = calendar.Add(Sam, oldKey, Est(11, 4, 6, 30), Est(11, 4, 6, 50));
        var tue = calendar.Add(Sam, otherKey, Est(11, 10, 6, 30), Est(11, 10, 6, 50));
        store.Save(WrittenKeys.Empty.With([oldKey, otherKey]));
        var snapshot = Snapshot();
        var facts = snapshot.Facts.ToDictionary(pair => pair.Key, pair => pair.Value);
        facts[Wed] = new DayFacts(118, null, new PlacedTime(Est(11, 4, 6, 30), Est(11, 4, 6, 50)));
        facts[NextTue] = new DayFacts(118, null, new PlacedTime(Est(11, 10, 6, 30), Est(11, 10, 6, 50)));
        snapshot = snapshot with { Facts = facts };

        var swap = SwapChoices.Check(snapshot, Wed, NextTue).Result!;
        var outcomes = await new WorkoutCalendarFollow(calendar, calendar, store).FollowSwapAsync(swap, CancellationToken.None);

        Assert.Equal([FollowOutcome.Moved, FollowOutcome.Moved], outcomes);
        Assert.Equal(otherKey, calendar.ById(wed).Key);
        Assert.Equal(oldKey, calendar.ById(tue).Key);
        Assert.True(WrittenKeysCodec.Decode(store.Json).Keys.Contains(otherKey));
    }
}
