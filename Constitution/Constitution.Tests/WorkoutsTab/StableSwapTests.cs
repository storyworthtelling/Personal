using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Screens;
using Constitution.Tests.Placement;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.WorkoutsTab.WorkoutScreenFixture;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>
/// B2 on the Workouts tab: a swap finds the events it moves by the keys they wear, and a key is the number the weekly run issued to
/// the session, not its place among its kind. The fixture's Tuesday and Thursday strength sessions were issued 4 and 5, not 0 and 1,
/// as they are once earlier re-plans of the week have come and gone.
/// </summary>
public class StableSwapTests
{
    private static readonly int Week = PlacementKey.WeekIndexOf(Tue);

    private static KeyIssue Issue(DateOnly day, SessionKind kind, int ordinal) => new(Sam, Week, kind, day.DayOfWeek, 0, ordinal);

    private static KeyAssignments TheRecord() => KeyAssignments.Empty.With(
    [
        Issue(Tue, SessionKind.Strength, 4),
        Issue(Thu, SessionKind.Strength, 5),
        Issue(Wed, SessionKind.Mobility, 2),
        Issue(Sat, SessionKind.Treadmill, 0),
        Issue(Sun, SessionKind.GentleWalk, 0),
    ]);

    private static PlacementKey Key(SessionKind kind, int ordinal) => PlacementKey.ForWorkout(Sam, kind, Week, ordinal);

    private static WorkoutsSnapshot Shown(KeyAssignments? record = null) => Snapshot() with { Assignments = record ?? TheRecord() };

    private sealed class Seeded
    {
        public FakeHeldCalendar Calendar { get; } = new();

        public MemoryKeyStore Written { get; } = new();

        public string Strength { get; }

        public string Treadmill { get; }

        public Seeded()
        {
            Strength = Calendar.Add(Sam, Key(SessionKind.Strength, 5), Est(11, 5, 16, 0), Est(11, 5, 16, 40));
            Treadmill = Calendar.Add(Sam, Key(SessionKind.Treadmill, 0), Est(11, 7, 8, 0), Est(11, 7, 8, 30));
            Written.Save(WrittenKeys.Empty.With([Key(SessionKind.Strength, 5), Key(SessionKind.Treadmill, 0)]));
        }

        public WorkoutCalendarFollow Follow(IKeyAssignmentStore? numbers = null) => new(Calendar, Calendar, Written, numbers);
    }

    // ---- the swap carries the record it was checked against ----

    [Fact]
    public void ASwapCarriesTheRecordItWasMadeAgainst()
    {
        var snapshot = Shown();

        var swap = SwapChoices.Check(snapshot, Thu, Sat).Result!;

        Assert.Same(snapshot.Assignments, swap.Assignments);
    }

    [Fact]
    public void AMoveIsNamedByTheNumbersIssued()
    {
        var swap = SwapChoices.Check(Shown(), Thu, Sat).Result!;

        var keys = swap.Moves.Select(move => WorkoutKeys.In(swap.Before, Sam, move.Old, swap.Assignments)).ToList();

        Assert.Equal([Key(SessionKind.Strength, 5), Key(SessionKind.Treadmill, 0)], keys);
    }

    // ---- the calendar follows by the same numbers ----

    [Fact]
    public async Task TheEventsAreFoundByTheNumbersIssuedAndRekeyedToTheNewPlace()
    {
        var week = new Seeded();
        var swap = SwapChoices.Check(Shown(), Thu, Sat).Result!;

        var outcomes = await week.Follow().FollowSwapAsync(swap, CancellationToken.None);

        // Thursday's strength goes to Saturday, a place that has no number yet: it takes the one after the highest issued (6).
        // Saturday's treadmill goes to Thursday, which has none either: the one after the highest of its kind (1).
        Assert.Equal([FollowOutcome.Moved, FollowOutcome.Moved], outcomes);
        Assert.Equal(Key(SessionKind.Strength, 6), week.Calendar.ById(week.Strength).Key);
        Assert.Equal(Key(SessionKind.Treadmill, 1), week.Calendar.ById(week.Treadmill).Key);
        Assert.True(WrittenKeysCodec.Decode(week.Written.Json).Keys.Contains(Key(SessionKind.Strength, 6)));
    }

    [Fact]
    public async Task TheNewPlacesNumbersAreKeptSoTheNextRunFindsThemIssued()
    {
        var week = new Seeded();
        var numbers = new MemoryKeyAssignments { Json = KeyAssignmentsCodec.Encode(TheRecord()) };
        var swap = SwapChoices.Check(Shown(), Thu, Sat).Result!;

        await week.Follow(numbers).FollowSwapAsync(swap, CancellationToken.None);

        var kept = KeyAssignmentsCodec.Decode(numbers.Json).Assignments;
        Assert.True(kept.Vouches(Key(SessionKind.Strength, 6)));
        Assert.True(kept.Vouches(Key(SessionKind.Treadmill, 1)));
        Assert.True(kept.Vouches(Key(SessionKind.Strength, 5)));
    }

    [Fact]
    public async Task TheFollowReadsTheRecordAsItNowStandsNotAsTheTabLoadedIt()
    {
        var week = new Seeded();
        var swap = SwapChoices.Check(Shown(), Thu, Sat).Result!;

        // A run issued Friday's strength (6) between the tab loading and the owner swapping.
        var now = TheRecord().With([Issue(Fri, SessionKind.Strength, 6)]);
        var numbers = new MemoryKeyAssignments { Json = KeyAssignmentsCodec.Encode(now) };

        await week.Follow(numbers).FollowSwapAsync(swap, CancellationToken.None);

        Assert.Equal(Key(SessionKind.Strength, 7), week.Calendar.ById(week.Strength).Key);
    }

    [Fact]
    public async Task AnUnreadableRecordOfNumbersBlocksTheEdit()
    {
        var week = new Seeded();
        var numbers = new MemoryKeyAssignments { Json = "not a record" };
        var swap = SwapChoices.Check(Shown(), Thu, Sat).Result!;

        var outcomes = await week.Follow(numbers).FollowSwapAsync(swap, CancellationToken.None);

        Assert.All(outcomes, outcome => Assert.Equal(FollowOutcome.Unavailable, outcome));
        Assert.Equal(0, week.Calendar.Edits);
        Assert.Equal("not a record", numbers.Json);
    }

    [Fact]
    public async Task ANumberAnotherEventStillWearsIsNotWornTwice()
    {
        var week = new Seeded();
        var record = TheRecord().With([Issue(Sat, SessionKind.Strength, 6)]);

        // An older Saturday strength session was dropped by a re-plan, but its event is still on the calendar under number 6.
        var stale = week.Calendar.Add(Sam, Key(SessionKind.Strength, 6), Est(11, 7, 15, 0), Est(11, 7, 15, 40));
        var swap = SwapChoices.Check(Shown(record), Thu, Sat).Result!;

        var outcomes = await week.Follow().FollowSwapAsync(swap, CancellationToken.None);

        Assert.Equal(FollowOutcome.Failed, outcomes[swap.Moves.ToList().FindIndex(move => move.Old.Session.Kind == SessionKind.Strength)]);
        Assert.Equal(Key(SessionKind.Strength, 5), week.Calendar.ById(week.Strength).Key);
        Assert.Equal(Est(11, 5, 16, 0), week.Calendar.ById(week.Strength).Start);
        Assert.Equal(Key(SessionKind.Strength, 6), week.Calendar.ById(stale).Key);
    }

    [Fact]
    public async Task WithNoRecordTheNamesAreTheOldCount()
    {
        var calendar = new FakeHeldCalendar();
        var written = new MemoryKeyStore();
        var strength = calendar.Add(Sam, Key(SessionKind.Strength, 1), Est(11, 5, 16, 0), Est(11, 5, 16, 40));
        written.Save(WrittenKeys.Empty.With(Key(SessionKind.Strength, 1)));
        var swap = SwapChoices.Check(Snapshot(), Thu, Sat).Result!;

        var outcomes = await new WorkoutCalendarFollow(calendar, calendar, written).FollowSwapAsync(swap, CancellationToken.None);

        Assert.Contains(FollowOutcome.Moved, outcomes);
        Assert.Equal(Key(SessionKind.Strength, 1), calendar.ById(strength).Key);
    }
}
