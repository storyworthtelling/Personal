using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Placement.PlacementFixture;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Placement;

/// <summary>
/// B2, the pure half. Sam plans strength on Monday, Wednesday and Friday and the first run writes all three. The owner then
/// adds a Monday meeting, so the re-plan from the week's start has Wednesday and Friday only. Numbered by place among its kind,
/// Wednesday became "strength 0" and Friday "strength 1": the engine found those keys held at Monday's and Wednesday's old times,
/// wrote nothing, left Friday's event orphaned under a key nobody wanted, and handed the pages the wrong events. Numbered from
/// what was issued, Wednesday and Friday keep the keys their events already wear.
/// </summary>
public class StableKeysEngineTests
{
    private static PlannedSession Strength(int day, params string[] who) => Planned(day, (16, 0), (18, 0), Effort(40), who.Length == 0 ? [Sam] : who);

    private static PlacementRequest Request(KeyAssignments? record, params PlannedSession[] sessions) =>
        PlaceRequest(Calendars(), PlanOf(sessions)) with { Assignments = record };

    private static PlacementKey Key(int ordinal, string person = Sam, SessionKind kind = SessionKind.Strength) =>
        PlacementKey.ForWorkout(person, kind, Week, ordinal);

    private sealed record OnCalendar(KeyAssignments Record, IReadOnlyList<HeldEvent> Held, WrittenKeys Written);

    // The first run of Monday, Wednesday and Friday, as the runner would leave it: every event written, every number issued.
    private static OnCalendar TheFirstWeek()
    {
        var plan = PlacementEngine.Plan(Request(KeyAssignments.Empty, Strength(0), Strength(2), Strength(4)), Fresh);
        Assert.Equal(3, plan.ToCreate.Count);
        return new OnCalendar(
            KeyAssignments.Empty.With(plan.Issued),
            plan.ToCreate.Select((make, index) => new HeldEvent($"event-{index}", make.Key, make.Start, make.End)).ToList(),
            WrittenKeys.Empty.With(plan.ToCreate.Select(make => make.Key)));
    }

    private static CalendarState State(OnCalendar week, params HeldEvent[] extra) =>
        new(week.Written, [.. week.Held, .. extra], new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static HeldEvent EventWith(OnCalendar week, int ordinal) => week.Held.Single(item => item.Key == Key(ordinal));

    // ---- the first plan ----

    [Fact]
    public void TheFirstPlanIssuesTheOldNumbers()
    {
        var plan = PlacementEngine.Plan(Request(KeyAssignments.Empty, Strength(0), Strength(2), Strength(4)), Fresh);

        Assert.Equal([Key(0), Key(1), Key(2)], plan.ToCreate.Select(make => make.Key));
        Assert.Equal([Key(0), Key(1), Key(2)], plan.Issued.Select(issue => issue.Key));
        Assert.Empty(plan.UncertainKinds);
    }

    [Fact]
    public void WhatTheRequestWantsIsNumberedByTheRecord()
    {
        var week = TheFirstWeek();

        var wanted = PlacementEngine.Wanted(Request(week.Record, Strength(2), Strength(4)));

        Assert.Equal([Key(1), Key(2)], wanted.Select(item => item.Key).Order(Comparer<PlacementKey>.Create((a, b) => string.CompareOrdinal(a.Value, b.Value))));
    }

    // ---- the replan that dropped Monday ----

    [Fact]
    public void ADroppedMondayLeavesWednesdayAndFridayWhereTheyAre()
    {
        var week = TheFirstWeek();

        var plan = PlacementEngine.Plan(Request(week.Record, Strength(2), Strength(4)), State(week));

        Assert.Empty(plan.ToCreate);
        Assert.Empty(plan.Issued);
        Assert.Empty(plan.UncertainKinds);
        Assert.Equal([Key(1), Key(2)], plan.Held.Select(held => held.Key).Order(Comparer<PlacementKey>.Create((a, b) => string.CompareOrdinal(a.Value, b.Value))));
        Assert.Equal(EventWith(week, 1).Start, Assert.IsType<EventHeld>(plan.For(Key(1))).Start);
        Assert.Equal(EventWith(week, 2).Start, Assert.IsType<EventHeld>(plan.For(Key(2))).Start);
    }

    [Fact]
    public void ANewDayAfterThatIsWrittenUnderANewNumber()
    {
        var week = TheFirstWeek();

        var plan = PlacementEngine.Plan(Request(week.Record, Strength(1), Strength(2), Strength(4)), State(week));

        var made = Assert.Single(plan.ToCreate);
        Assert.Equal(Key(3), made.Key);
        Assert.Equal([Key(3)], plan.Issued.Select(issue => issue.Key));
        Assert.IsType<EventHeld>(plan.For(Key(1)));
        Assert.IsType<EventHeld>(plan.For(Key(2)));
    }

    [Fact]
    public void AnEventTheOwnerMovedToAnotherDayStaysWhereTheyPutIt()
    {
        var week = TheFirstWeek();
        var moved = EventWith(week, 1) with { Start = EventWith(week, 1).Start.AddDays(1), End = EventWith(week, 1).End.AddDays(1) };
        var state = new CalendarState(week.Written, [EventWith(week, 0), moved, EventWith(week, 2)], new HashSet<string>());

        var plan = PlacementEngine.Plan(Request(week.Record, Strength(0), Strength(2), Strength(4)), state);

        Assert.Empty(plan.ToCreate);
        Assert.Empty(plan.UncertainKinds);
        Assert.Equal(moved.Start, Assert.IsType<EventHeld>(plan.For(Key(1))).Start);
    }

    [Fact]
    public void ADeletedEventIsStillASkip()
    {
        var week = TheFirstWeek();
        var state = new CalendarState(week.Written, [EventWith(week, 0), EventWith(week, 2)], new HashSet<string>());

        var plan = PlacementEngine.Plan(Request(week.Record, Strength(0), Strength(2), Strength(4)), state);

        Assert.Equal([Key(1)], plan.Skipped.Select(skipped => skipped.Key));
        Assert.Empty(plan.ToCreate);
        Assert.Empty(plan.UncertainKinds);
    }

    // ---- when the record cannot vouch ----

    [Fact]
    public void ALostRecordAndAShiftedWeekIsUnknownNotGuessed()
    {
        var week = TheFirstWeek();

        // The record is gone and Monday was dropped: Wednesday would be "0" and Friday "1", held at Monday's and Wednesday's times.
        var plan = PlacementEngine.Plan(Request(KeyAssignments.Empty, Strength(2), Strength(4)), State(week));

        Assert.Empty(plan.ToCreate);
        Assert.Empty(plan.Held);
        Assert.Empty(plan.Skipped);
        Assert.Empty(plan.Issued);
        Assert.Equal(2, plan.Unplaced.Count);
        Assert.All(plan.Unplaced, item => Assert.Equal(UnplacedReason.KeysUncertain, item.Reason));
        var unknown = Assert.Single(plan.UncertainKinds);
        Assert.Equal(Sam, unknown.Person, ignoreCase: true);
        Assert.Equal(SessionKind.Strength, unknown.Kind);
        Assert.False(string.IsNullOrWhiteSpace(unknown.Why));
    }

    [Fact]
    public void ALostRecordAndTheSameWeekIsAdopted()
    {
        var week = TheFirstWeek();

        var plan = PlacementEngine.Plan(Request(KeyAssignments.Empty, Strength(0), Strength(2), Strength(4)), State(week));

        Assert.Empty(plan.ToCreate);
        Assert.Equal(3, plan.Held.Count);
        Assert.Empty(plan.UncertainKinds);
        Assert.Equal([Key(0), Key(1), Key(2)], plan.Issued.Select(issue => issue.Key));
    }

    [Fact]
    public void AnEventNobodyIssuedAKeyForMakesTheKindUnknown()
    {
        var week = TheFirstWeek();
        var stray = new HeldEvent("event-stray", Key(5), At(1, 17), At(1, 17, 40));

        var plan = PlacementEngine.Plan(Request(week.Record, Strength(0), Strength(2), Strength(4)), State(week, stray));

        Assert.Empty(plan.ToCreate);
        Assert.Equal(3, plan.Unplaced.Count(item => item.Reason == UnplacedReason.KeysUncertain));
        Assert.Equal(SessionKind.Strength, Assert.Single(plan.UncertainKinds).Kind);
    }

    [Fact]
    public void AnEventTheRecordRetiredIsNotAStray()
    {
        var week = TheFirstWeek();

        // Monday's number was issued, so its event is explained: it is simply no longer wanted.
        var plan = PlacementEngine.Plan(Request(week.Record, Strength(2), Strength(4)), State(week));

        Assert.Empty(plan.UncertainKinds);
        Assert.Equal(2, plan.Held.Count);
    }

    [Fact]
    public void AWrittenKeyNobodyVouchesForIsNotReadAsADelete()
    {
        var week = TheFirstWeek();
        var state = new CalendarState(week.Written, [], new HashSet<string>());

        var plan = PlacementEngine.Plan(Request(KeyAssignments.Empty, Strength(0), Strength(2), Strength(4)), state);

        Assert.Empty(plan.Skipped);
        Assert.Empty(plan.ToCreate);
        Assert.Equal(3, plan.Unplaced.Count(item => item.Reason == UnplacedReason.KeysUncertain));
    }

    [Fact]
    public void ASharedSessionWaitsAsAWhole()
    {
        var walk = PlannedAt(6, (10, 0), (11, 0), (10, 0), Effort(40, false, SessionKind.GentleWalk), Together.FamilyWalk, Sam, Lee);
        var stray = new HeldEvent("event-walk", Key(0, Sam, SessionKind.GentleWalk), At(1, 10), At(1, 10, 40));

        var plan = PlacementEngine.Plan(Request(KeyAssignments.Empty, walk), new CalendarState(WrittenKeys.Empty, [stray], new HashSet<string>()));

        Assert.Empty(plan.ToCreate);
        Assert.Equal(2, plan.Unplaced.Count(item => item.Reason == UnplacedReason.KeysUncertain));
        Assert.Contains(plan.Unplaced, item => item.Person == Lee);
    }

    [Fact]
    public void OtherPeopleAndOtherKindsCarryOn()
    {
        var week = TheFirstWeek();
        var mobility = Planned(1, (16, 0), (18, 0), Effort(20, false, SessionKind.Mobility), Sam);
        var lees = Strength(3, Lee);

        var plan = PlacementEngine.Plan(Request(KeyAssignments.Empty, Strength(2), Strength(4), mobility, lees), State(week));

        Assert.Equal(SessionKind.Strength, Assert.Single(plan.UncertainKinds).Kind);
        Assert.Equal(
            [Key(0, Lee), Key(0, Sam, SessionKind.Mobility)],
            plan.ToCreate.Select(make => make.Key).Order(Comparer<PlacementKey>.Create((a, b) => string.CompareOrdinal(a.Value, b.Value))));
        Assert.Equal(2, plan.Issued.Count);
    }

    [Fact]
    public void NumbersForSomeoneWhoseCalendarWasNotLookedIntoAreNotKept()
    {
        // Kept, they would later vouch for events the engine never met, and an old event on another day would read as the owner's move.
        var state = new CalendarState(WrittenKeys.Empty, [], new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Lee });

        var plan = PlacementEngine.Plan(Request(KeyAssignments.Empty, Strength(0), Strength(1, Lee)), state);

        Assert.Equal([Key(0)], plan.Issued.Select(issue => issue.Key));
        Assert.Equal(UnplacedReason.CalendarUnavailable, Assert.Single(plan.Unplaced).Reason);
    }

    [Fact]
    public void WithoutARecordTheEngineIsAsItWas()
    {
        var week = TheFirstWeek();

        var plan = PlacementEngine.Plan(Request(null, Strength(2), Strength(4)), State(week));

        Assert.Empty(plan.UncertainKinds);
        Assert.Empty(plan.Issued);
        Assert.Equal(2, plan.Held.Count);
    }

    // ---- the week ----

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void AWeekThatDoesNotBeginOnAMondayIsRefused(int dayOffset)
    {
        var request = Request(KeyAssignments.Empty, Strength(2)) with { WeekStart = Day(dayOffset) };

        var plan = Assert.Throws<ArgumentException>(() => PlacementEngine.Plan(request, Fresh));
        var wanted = Assert.Throws<ArgumentException>(() => PlacementEngine.Wanted(request));

        Assert.Contains("Monday", plan.Message, StringComparison.Ordinal);
        Assert.Contains("Monday", wanted.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMondayIsAccepted()
    {
        var plan = PlacementEngine.Plan(Request(KeyAssignments.Empty, Strength(2)), Fresh);

        Assert.Single(plan.ToCreate);
    }
}
