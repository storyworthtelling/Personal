using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Placement.PlacementFixture;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Placement;

/// <summary>
/// B2, the half that touches a calendar: the runner reads the record of issued numbers, plans by it, and keeps what the plan
/// issued before it writes anything, so a run that dies between writing and saving can never leave an event wearing a number
/// no record has heard of.
/// </summary>
public class StableRunnerTests
{
    private static PlannedSession Strength(int day) => Planned(day, (16, 0), (18, 0), Effort(40), Sam);

    private static PlacementRequest Week(params int[] days) => PlaceRequest(Calendars(), PlanOf(days.Select(Strength).ToArray()));

    private static PlacementKey Key(int ordinal) => StrengthKey(Sam, ordinal);

    private sealed class Scenario
    {
        public FakeCalendar Calendar { get; } = new();

        public MemoryKeyStore Written { get; } = new();

        public MemoryKeyAssignments Issued { get; } = new();

        public Task<PlacementRun> RunAsync(PlacementRequest request, ICalendarWriter? writer = null) =>
            new PlacementRunner(writer ?? Calendar, Written, Issued).RunAsync(request, CancellationToken.None);
    }

    private sealed class ExplodingWriter(ICalendarWriter inner) : ICalendarWriter
    {
        public Task<CalendarLookup> FindAsync(string person, IReadOnlyList<PlacementKey> keys, CancellationToken cancellationToken) =>
            inner.FindAsync(person, keys, cancellationToken);

        public Task<CalendarCreate> CreateAsync(EventToCreate placed, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The calendar fell over while writing.");
    }

    [Fact]
    public async Task ADroppedMondayIsHeldNotWrittenAgain()
    {
        var week = new Scenario();
        await week.RunAsync(Week(0, 2, 4));
        var snapshot = week.Calendar.Snapshot();

        var second = await week.RunAsync(Week(2, 4));

        Assert.Empty(second.Created);
        Assert.Equal([Key(1), Key(2)], second.Plan!.Held.Select(held => held.Key).Order(Comparer<PlacementKey>.Create((a, b) => string.CompareOrdinal(a.Value, b.Value))));
        Assert.Equal(snapshot, week.Calendar.Snapshot());
    }

    [Fact]
    public async Task ANewDayIsWrittenOnceWhateverNumbersAreRetired()
    {
        var week = new Scenario();
        await week.RunAsync(Week(0, 2, 4));

        var second = await week.RunAsync(Week(1, 2, 4));
        var third = await week.RunAsync(Week(1, 2, 4));

        Assert.Equal(Key(3), Assert.Single(second.Created).Key);
        Assert.Empty(third.Created);
        Assert.Equal(4, week.Calendar.All.Count);
        Assert.Equal(4, week.Calendar.All.Select(item => item.Key).Distinct().Count());
    }

    [Fact]
    public async Task TheFirstRunKeepsEachNumberItIssued()
    {
        var week = new Scenario();

        await week.RunAsync(Week(0, 2, 4));

        var record = KeyAssignmentsCodec.Decode(week.Issued.Json);
        Assert.True(record.IsReadable);
        Assert.Equal([Key(0), Key(1), Key(2)], record.Assignments.Issues.Select(issue => issue.Key));
    }

    [Fact]
    public async Task TheNumbersAreKeptBeforeTheFirstWrite()
    {
        var week = new Scenario();

        await Assert.ThrowsAsync<InvalidOperationException>(() => week.RunAsync(Week(0, 2, 4), new ExplodingWriter(week.Calendar)));

        // The calendar fell over before one event was written, and the numbers are kept anyway: an event written after this
        // would always find its number in the record.
        Assert.Equal(3, KeyAssignmentsCodec.Decode(week.Issued.Json).Assignments.Issues.Count);
        Assert.Null(week.Written.Json);
    }

    [Fact]
    public async Task AnUnchangedWeekSavesNothingMore()
    {
        var week = new Scenario();
        await week.RunAsync(Week(0, 2, 4));
        var saves = week.Issued.Saves;

        await week.RunAsync(Week(0, 2, 4));
        await week.RunAsync(Week(2, 4));

        Assert.Equal(saves, week.Issued.Saves);
    }

    [Fact]
    public async Task AnUnreadableRecordOfNumbersBlocksTheRunBeforeTheCalendarIsTouched()
    {
        var week = new Scenario { Issued = { Json = "{ not the record" } };

        var run = await week.RunAsync(Week(0, 2, 4));

        Assert.NotNull(run.BlockedBecause);
        Assert.Contains("issued", run.BlockedBecause, StringComparison.Ordinal);
        Assert.Null(run.Plan);
        Assert.Equal(0, week.Calendar.FindCalls);
        Assert.Equal(0, week.Calendar.CreateCalls);
        Assert.Equal("{ not the record", week.Issued.Json);
        Assert.Equal(0, week.Issued.Saves);
    }

    [Fact]
    public async Task ALostRecordAndAShiftedWeekWritesNothingAndKeepsNothing()
    {
        var week = new Scenario();
        await week.RunAsync(Week(0, 2, 4));
        week.Issued.Json = null;
        var saves = week.Issued.Saves;
        var snapshot = week.Calendar.Snapshot();

        var run = await week.RunAsync(Week(2, 4));

        Assert.Empty(run.Created);
        Assert.Equal(SessionKind.Strength, Assert.Single(run.Plan!.UncertainKinds).Kind);
        Assert.Equal(snapshot, week.Calendar.Snapshot());
        Assert.Equal(saves, week.Issued.Saves);
        Assert.Null(week.Issued.Json);
    }

    [Fact]
    public async Task WithoutAStoreTheRequestsOwnRecordIsUsed()
    {
        var calendar = new FakeCalendar();
        var written = new MemoryKeyStore();
        var first = SessionKeys.For(PlanOf(Strength(0), Strength(2), Strength(4)), KeyAssignments.Empty);
        var request = Week(2, 4) with { Assignments = first.Assignments };

        var run = await new PlacementRunner(calendar, written).RunAsync(request, CancellationToken.None);

        Assert.Equal([Key(1), Key(2)], run.Created.Select(make => make.Key).Order(Comparer<PlacementKey>.Create((a, b) => string.CompareOrdinal(a.Value, b.Value))));
    }
}
