using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Placement.PlacementFixture;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Placement;

/// <summary>
/// The runner is the part that touches a calendar: it reads the record, looks events up by key, plans, and
/// writes what is new. These pin R8 (a re-run on an unchanged week writes nothing) and the ways a run can go
/// wrong without anyone being told: a calendar that cannot be looked into, a write that fails, a record that
/// cannot be read.
/// </summary>
public class PlacementRunnerTests
{
    private static PlacementRequest Week(params HouseholdEventRequest[] more) =>
        PlaceRequest(
            Calendars(),
            PlanOf(Planned(1, (16, 0), (18, 0), Effort(40), Sam), Planned(3, (16, 0), (18, 0), Effort(20, false, SessionKind.Mobility), Sam)),
            [Cook(Lee, 2, (16, 0), (17, 30), 45), .. more]);

    private static Task<PlacementRun> RunAsync(FakeCalendar calendar, MemoryKeyStore store, PlacementRequest request, CancellationToken token = default) =>
        new PlacementRunner(calendar, store).RunAsync(request, token);

    [Fact]
    public async Task ARunWritesEachEventOnce()
    {
        var calendar = new FakeCalendar();
        var store = new MemoryKeyStore();

        var run = await RunAsync(calendar, store, Week());

        Assert.Equal(3, run.Created.Count);
        Assert.Equal(3, calendar.All.Count);
        Assert.Equal(
            ["Cook: chili", "Workout · 20 min", "Workout · 40 min"],
            calendar.All.Select(item => item.Title).Order());
        Assert.Equal(3, calendar.All.Select(item => item.Key).Distinct().Count());
        var record = WrittenKeysCodec.Decode(store.Json);
        Assert.True(record.IsReadable);
        Assert.All(calendar.All, item => Assert.True(record.Keys.Contains(item.Key)));
        Assert.Empty(run.Failed);
        Assert.Null(run.BlockedBecause);
    }

    [Fact]
    public async Task AReRunOnAnUnchangedWeekWritesNothing()
    {
        var calendar = new FakeCalendar();
        var store = new MemoryKeyStore();
        await RunAsync(calendar, store, Week());
        var snapshot = calendar.Snapshot();
        var creates = calendar.CreateCalls;
        var saves = store.Saves;

        for (var again = 0; again < 3; again++)
        {
            var run = await RunAsync(calendar, store, Week());

            Assert.Empty(run.Created);
            Assert.Empty(run.Plan!.ToCreate);
            Assert.Equal(3, run.Plan.Held.Count);
        }

        Assert.Equal(creates, calendar.CreateCalls);
        Assert.Equal(snapshot, calendar.Snapshot());
        Assert.Equal(saves, store.Saves);
    }

    [Fact]
    public async Task AFailedWriteIsRetriedNotSkipped()
    {
        var calendar = new FakeCalendar();
        calendar.FailingPeople.Add(Lee);
        var store = new MemoryKeyStore();

        var first = await RunAsync(calendar, store, Week());

        var failure = Assert.Single(first.Failed);
        Assert.Equal(Lee, failure.Event.Person);
        Assert.Contains("refused", failure.Reason, StringComparison.Ordinal);
        Assert.Equal(2, first.Created.Count);
        var cookKey = HouseholdKey(Lee, "dinner-a");
        Assert.False(WrittenKeysCodec.Decode(store.Json).Keys.Contains(cookKey), "A write that failed was recorded as written.");

        // Next time the calendar answers; the cook was never written, so it is written now, not skipped.
        calendar.FailingPeople.Clear();
        var second = await RunAsync(calendar, store, Week());

        Assert.Equal(cookKey, Assert.Single(second.Created).Key);
        Assert.Empty(second.Plan!.Skipped);
    }

    [Fact]
    public async Task ACalendarThatCannotBeLookedIntoIsLeftAlone()
    {
        // Lee has not shared hers yet (R9). Nothing is written to it, and nothing is concluded about it.
        var calendar = new FakeCalendar();
        calendar.UnavailablePeople.Add(Lee);
        var store = new MemoryKeyStore();
        var walk = PlannedAt(6, (10, 0), (11, 0), (10, 0), Effort(40, false, SessionKind.GentleWalk), Together.FamilyWalk, Sam, Lee);
        PlacementRequest Request() => PlaceRequest(
            Calendars(),
            PlanOf(Planned(1, (16, 0), (18, 0), Effort(40), Sam), walk),
            Cook(Lee, 2, (16, 0), (17, 30), 45));

        var first = await RunAsync(calendar, store, Request());

        var sams = Assert.Single(first.Created);
        Assert.Equal(Sam, sams.Person);
        Assert.Equal(3, first.Plan!.Unplaced.Count);
        Assert.All(first.Plan.Unplaced, item => Assert.Equal(UnplacedReason.CalendarUnavailable, item.Reason));
        Assert.Contains(first.Plan.Unplaced, item => item.Person == Lee && item.Title == "Cook: chili");
        Assert.Contains(first.Plan.Unplaced, item => item.Person == Sam && item.Title == "Workout · 40 min");
        Assert.Empty(calendar.Of(Lee));
        Assert.Empty(first.Plan.Skipped);

        // The share is done: now all of it is written, and nothing was ever treated as deleted.
        calendar.UnavailablePeople.Clear();
        var second = await RunAsync(calendar, store, Request());

        Assert.Equal(3, second.Created.Count);
        Assert.Empty(second.Plan!.Skipped);
        Assert.Equal(2, calendar.Of(Lee).Count);
    }

    [Fact]
    public async Task ARecordThatCannotBeReadWritesNothing()
    {
        var calendar = new FakeCalendar();
        const string Garbled = "{ this is not the record";
        var store = new MemoryKeyStore { Json = Garbled };

        var run = await RunAsync(calendar, store, Week());

        Assert.False(string.IsNullOrWhiteSpace(run.BlockedBecause));
        Assert.Null(run.Plan);
        Assert.Empty(run.Created);
        Assert.Equal(0, calendar.CreateCalls);
        Assert.Equal(0, calendar.FindCalls);
        // And the unreadable record is left as it was, not replaced by an empty one.
        Assert.Equal(Garbled, store.Json);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task AnEventOnTheCalendarHealsALostRecord()
    {
        // An earlier run wrote the events and died before it could save the record. The events carry their
        // keys, so the next run finds them, takes them as written, and a later delete still means a skip.
        var calendar = new FakeCalendar();
        var store = new MemoryKeyStore();
        await RunAsync(calendar, store, Week());
        store.Json = null;
        var creates = calendar.CreateCalls;

        var healed = await RunAsync(calendar, store, Week());

        Assert.Empty(healed.Created);
        Assert.Equal(creates, calendar.CreateCalls);
        var key = StrengthKey(Sam);
        Assert.True(WrittenKeysCodec.Decode(store.Json).Keys.Contains(key));

        calendar.OwnerDeletes(key, Sam);
        var after = await RunAsync(calendar, store, Week());

        Assert.Equal(key, Assert.Single(after.Plan!.Skipped).Key);
        Assert.Empty(after.Created);
    }

    [Fact]
    public async Task ARunThatCannotLookIntoAnyCalendarWritesNothing()
    {
        var calendar = new FakeCalendar();
        calendar.UnavailablePeople.Add(Sam);
        calendar.UnavailablePeople.Add(Lee);
        var store = new MemoryKeyStore();

        var run = await RunAsync(calendar, store, Week());

        Assert.Empty(run.Created);
        Assert.Equal(0, calendar.CreateCalls);
        Assert.Equal(3, run.Plan!.Unplaced.Count);
        Assert.All(run.Plan.Unplaced, item => Assert.Equal(UnplacedReason.CalendarUnavailable, item.Reason));
    }

    [Fact]
    public async Task ACancelledRunWritesNothing()
    {
        var calendar = new FakeCalendar();
        var store = new MemoryKeyStore();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(calendar, store, Week(), cancelled.Token));

        Assert.Equal(0, calendar.CreateCalls);
        Assert.Empty(calendar.All);
    }

    [Fact]
    public async Task ASessionTheCalendarHasNoRoomForIsNeverWritten()
    {
        var calendar = new FakeCalendar();
        var store = new MemoryKeyStore();
        var tight = PlaceRequest(Calendars(), PlanOf(Planned(1, (16, 0), (16, 50), Effort(40), Sam)));

        var run = await RunAsync(calendar, store, tight);

        Assert.Empty(run.Created);
        Assert.Empty(calendar.All);
        Assert.Equal(UnplacedReason.NoRoomForShowerBuffer, Assert.Single(run.Plan!.Unplaced).Reason);
        // A refusal is not a write: nothing is recorded, so a roomier week can still place it.
        Assert.False(WrittenKeysCodec.Decode(store.Json).Keys.Contains(StrengthKey(Sam)));
    }
}
