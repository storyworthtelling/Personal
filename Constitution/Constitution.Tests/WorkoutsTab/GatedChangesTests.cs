using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Screens;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.Placement;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.WorkoutsTab.WorkoutScreenFixture;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>
/// The Workouts tab's swap, skip and retry edit calendar events and the written-keys record, as the meal run, the workout run and the deadline
/// step do, so they take turns with those through the one shared gate. A gate that stays busy is a typed sentence the tab shows under its
/// header, never a hang, and nothing is written.
/// </summary>
public class GatedChangesTests
{
    private static readonly TimeSpan Plenty = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(60);
    private static readonly int Week = PlacementKey.WeekIndexOf(Tue);

    private sealed class Source(List<string> order) : IWorkoutsSource
    {
        public List<WorkoutsChange> Recorded { get; } = [];

        public WorkoutsRead Read(DateTimeOffset now) => new WorkoutsRead.NoPlanYet();

        public void Record(WorkoutsChange change)
        {
            order.Add("record");
            Recorded.Add(change);
        }
    }

    // The edit door, which says when an edit starts and ends and takes a moment, so two changes running at once would overlap.
    private sealed class Door(FakeHeldCalendar calendar, List<string> order) : IWorkoutEventEditor
    {
        private int inside;

        public int Most { get; private set; }

        public async Task<EventEdit> MoveAsync(string person, string eventId, PlacementKey newKey, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
        {
            Enter("move");
            try
            {
                await Task.Delay(15, cancellationToken);
                return await calendar.MoveAsync(person, eventId, newKey, start, end, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref inside);
            }
        }

        public async Task<EventEdit> RemoveAsync(string person, string eventId, CancellationToken cancellationToken)
        {
            Enter("remove");
            try
            {
                await Task.Delay(15, cancellationToken);
                return await calendar.RemoveAsync(person, eventId, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref inside);
            }
        }

        private void Enter(string word)
        {
            order.Add(word);
            Most = Math.Max(Most, Interlocked.Increment(ref inside));
        }
    }

    private sealed class Rig
    {
        public List<string> Order { get; } = [];

        public RunGate Gate { get; } = new();

        public FakeHeldCalendar Calendar { get; } = new();

        public MemoryKeyStore Written { get; } = new();

        public Source Source { get; }

        public Door Door { get; }

        public Rig()
        {
            Source = new Source(Order);
            Door = new Door(Calendar, Order);
            var strengthKey = PlacementKey.ForWorkout(Sam, SessionKind.Strength, Week, 1);
            var treadmillKey = PlacementKey.ForWorkout(Sam, SessionKind.Treadmill, Week, 0);
            Calendar.Add(Sam, strengthKey, Est(11, 5, 16, 0), Est(11, 5, 16, 40));
            Calendar.Add(Sam, treadmillKey, Est(11, 7, 8, 0), Est(11, 7, 8, 30));
            Written.Save(WrittenKeys.Empty.With([strengthKey, treadmillKey]));
        }

        public GatedWorkoutChanges Changes(TimeSpan wait) =>
            new(new WorkoutChanges(Source, new WorkoutCalendarFollow(Calendar, Door, Written)), Gate, wait);

        // Another run (the meal run, say) has the calendars; returns when it is inside, and the gate frees when the returned source is set.
        public async Task<TaskCompletionSource> HoldAsync()
        {
            var release = new TaskCompletionSource();
            var inside = new TaskCompletionSource();
            _ = Gate.RunAsync(async _ =>
            {
                inside.SetResult();
                await release.Task;
                Order.Add("other run done");
                return 0;
            }, Plenty, CancellationToken.None);
            await inside.Task;
            return release;
        }
    }

    private static SwapResult Swap() => SwapChoices.Check(Snapshot(), Thu, Sat).Result!;

    private static PendingSkip Skip() =>
        new(Sam, Thu, SessionKind.Strength, ThuStrength(), new PlacedTime(Est(11, 5, 16, 0), Est(11, 5, 16, 40)), PlacementKey.ForWorkout(Sam, SessionKind.Strength, Week, 1), Est(11, 3, 12));

    [Fact]
    public async Task OnAFreeGateASwapIsKeptAndSaysNothing()
    {
        var rig = new Rig();

        var result = await rig.Changes(Plenty).SwapAsync(Swap(), CancellationToken.None);

        Assert.True(result.Kept);
        Assert.False(result.Busy);
        Assert.Null(result.Sentence);
        Assert.Single(rig.Source.Recorded);
    }

    [Fact]
    public async Task ASwapWaitsForTheRunThatHoldsTheGate()
    {
        var rig = new Rig();
        var release = await rig.HoldAsync();

        var swap = rig.Changes(Plenty).SwapAsync(Swap(), CancellationToken.None);
        await Task.Delay(120);
        Assert.False(swap.IsCompleted);
        Assert.Empty(rig.Order);
        release.SetResult();
        var result = await swap;

        Assert.True(result.Kept);
        Assert.Equal("other run done", rig.Order[0]);
        Assert.Equal("record", rig.Order[1]);
    }

    [Fact]
    public async Task ASkipWaitsForTheRunThatHoldsTheGate()
    {
        var rig = new Rig();
        var release = await rig.HoldAsync();

        var skip = rig.Changes(Plenty).SkipAsync(Skip(), CancellationToken.None);
        await Task.Delay(120);
        Assert.False(skip.IsCompleted);
        release.SetResult();
        var result = await skip;

        Assert.True(result.Kept);
        Assert.Equal("other run done", rig.Order[0]);
    }

    [Fact]
    public async Task ABusyGateGivesATypedSentenceForASwapAndWritesNothing()
    {
        var rig = new Rig();
        var release = await rig.HoldAsync();

        var result = await rig.Changes(Brief).SwapAsync(Swap(), CancellationToken.None);

        Assert.True(result.Busy);
        Assert.False(result.Kept);
        Assert.Equal(FollowWords.GateBusy, result.Sentence);
        Assert.Null(result.Failure);
        Assert.Empty(rig.Source.Recorded);
        Assert.Equal(0, rig.Calendar.Edits);
        Assert.Equal(0, rig.Calendar.FindCalls);
        release.SetResult();
    }

    [Fact]
    public async Task ABusyGateGivesATypedSentenceForASkipAndWritesNothing()
    {
        var rig = new Rig();
        var release = await rig.HoldAsync();

        var result = await rig.Changes(Brief).SkipAsync(Skip(), CancellationToken.None);

        Assert.True(result.Busy);
        Assert.False(result.Kept);
        Assert.Equal(FollowWords.GateBusy, result.Sentence);
        Assert.Empty(rig.Source.Recorded);
        Assert.Equal(0, rig.Calendar.Edits);
        release.SetResult();
    }

    [Fact]
    public async Task ABusyGateLeavesAnOwedChangeOwedAndTheCalendarUntouched()
    {
        var rig = new Rig();
        rig.Calendar.Unavailable = true;
        var changes = rig.Changes(Brief);
        await changes.SwapAsync(Swap(), CancellationToken.None);
        Assert.True(changes.HasOwed);
        var finds = rig.Calendar.FindCalls;
        var release = await rig.HoldAsync();

        var retry = await changes.RetryAsync(CancellationToken.None);

        Assert.True(retry.Busy);
        Assert.Equal(FollowWords.GateBusy, retry.Sentence);
        Assert.True(changes.HasOwed);
        Assert.Equal(finds, rig.Calendar.FindCalls);
        release.SetResult();
    }

    [Fact]
    public async Task TheRetryRunsOnceTheGateIsFree()
    {
        var rig = new Rig();
        rig.Calendar.Unavailable = true;
        var changes = rig.Changes(Plenty);
        await changes.SwapAsync(Swap(), CancellationToken.None);
        rig.Calendar.Unavailable = false;

        var retry = await changes.RetryAsync(CancellationToken.None);

        Assert.False(retry.Busy);
        Assert.False(changes.HasOwed);
    }

    [Fact]
    public async Task ASwapAndASkipHeldBehindARunNeverInterleave()
    {
        var rig = new Rig();
        var release = await rig.HoldAsync();
        // Two separate change objects so only the gate, not a lock inside one of them, can keep them apart.
        var swap = rig.Changes(Plenty).SwapAsync(Swap(), CancellationToken.None);
        var skip = rig.Changes(Plenty).SkipAsync(Skip(), CancellationToken.None);
        await Task.Delay(120);
        Assert.Empty(rig.Order);

        release.SetResult();
        await Task.WhenAll(swap, skip);

        Assert.Equal(1, rig.Door.Most);
        Assert.Equal("other run done", rig.Order[0]);
        // Written down, moved, written down: the skip's record came after every one of the swap's edits.
        Assert.Equal(["other run done", "record", "move", "move", "record"], rig.Order);
    }

    [Fact]
    public async Task AWaitingChangeThatIsCancelledLeavesWithNothingWritten()
    {
        var rig = new Rig();
        var release = await rig.HoldAsync();
        using var cancel = new CancellationTokenSource();

        var swap = rig.Changes(Plenty).SwapAsync(Swap(), cancel.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => swap);
        Assert.Empty(rig.Source.Recorded);
        release.SetResult();
    }

    [Fact]
    public void TheBusySentenceIsPlainAndOnlyAsksForAMinute()
    {
        Assert.Equal("Another plan is being written; try again in a minute.", FollowWords.GateBusy);
    }
}
