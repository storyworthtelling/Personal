using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Screens;
using Constitution.Tests.Placement;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.WorkoutsTab.WorkoutScreenFixture;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>
/// S2: what happens after the owner swaps or skips on the Workouts tab, in the order that keeps them honest. The change is written down
/// first; the calendar is asked to follow second, and what it answers is read into a sentence for the tab, never dropped. A change that could
/// not be written down says so and is not left looking done. A calendar that did not change, and has not moved anything, is asked again.
/// </summary>
public class WorkoutChangesTests
{
    private static readonly int Week = PlacementKey.WeekIndexOf(Tue);

    private sealed class Source : IWorkoutsSource
    {
        public List<WorkoutsChange> Recorded { get; } = [];

        public Exception? Fails { get; set; }

        public WorkoutsRead Read(DateTimeOffset now) => new WorkoutsRead.NoPlanYet();

        public void Record(WorkoutsChange change)
        {
            if (Fails is not null)
            {
                throw Fails;
            }

            Recorded.Add(change);
        }
    }

    /// <summary>The calendar's edit door, which can be made to refuse or to fall over for one event.</summary>
    private sealed class Door(FakeHeldCalendar calendar) : IWorkoutEventEditor
    {
        public HashSet<string> Refuses { get; } = [];

        public Exception? Throws { get; set; }

        public Task<EventEdit> MoveAsync(string person, string eventId, PlacementKey newKey, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
        {
            if (Throws is not null)
            {
                throw Throws;
            }

            return Refuses.Contains(eventId)
                ? Task.FromResult(EventEdit.Failed("The calendar said no."))
                : calendar.MoveAsync(person, eventId, newKey, start, end, cancellationToken);
        }

        public Task<EventEdit> RemoveAsync(string person, string eventId, CancellationToken cancellationToken)
        {
            if (Throws is not null)
            {
                throw Throws;
            }

            return Refuses.Contains(eventId)
                ? Task.FromResult(EventEdit.Failed("The calendar said no."))
                : calendar.RemoveAsync(person, eventId, cancellationToken);
        }
    }

    private sealed class Week0
    {
        public FakeHeldCalendar Calendar { get; } = new();

        public MemoryKeyStore Written { get; } = new();

        public Door Door { get; }

        public Source Source { get; } = new();

        public string Strength { get; }

        public string Treadmill { get; }

        public Week0()
        {
            Door = new Door(Calendar);
            var strengthKey = PlacementKey.ForWorkout(Sam, SessionKind.Strength, Week, 1);
            var treadmillKey = PlacementKey.ForWorkout(Sam, SessionKind.Treadmill, Week, 0);
            Strength = Calendar.Add(Sam, strengthKey, Est(11, 5, 16, 0), Est(11, 5, 16, 40));
            Treadmill = Calendar.Add(Sam, treadmillKey, Est(11, 7, 8, 0), Est(11, 7, 8, 30));
            Written.Save(WrittenKeys.Empty.With([strengthKey, treadmillKey]));
        }

        public WorkoutChanges Changes() => new(Source, new WorkoutCalendarFollow(Calendar, Door, Written));
    }

    private static SwapResult Swap() => SwapChoices.Check(Snapshot(), Thu, Sat).Result!;

    private static PendingSkip Skip(PlacementKey? key = null) =>
        new(Sam, Thu, SessionKind.Strength, ThuStrength(), new PlacedTime(Est(11, 5, 16, 0), Est(11, 5, 16, 40)), key ?? PlacementKey.ForWorkout(Sam, SessionKind.Strength, Week, 1), Est(11, 3, 12));

    // ---- a swap ----

    [Fact]
    public async Task ASwapThatWentThroughSaysNothing()
    {
        var week = new Week0();
        var changes = week.Changes();
        var swap = Swap();

        var result = await changes.SwapAsync(swap, CancellationToken.None);

        Assert.True(result.Kept);
        Assert.Null(result.Sentence);
        Assert.False(changes.HasOwed);
        Assert.IsType<WorkoutsChange.Swapped>(Assert.Single(week.Source.Recorded));
        Assert.Equal(swap.Moves.Single(move => move.Old.Session.Kind == SessionKind.Strength).New.Start, week.Calendar.ById(week.Strength).Start);
    }

    [Fact]
    public async Task ASwapThatCouldNotBeSavedIsNotKeptAndTheCalendarIsLeftAlone()
    {
        var week = new Week0();
        week.Source.Fails = new IOException("The disk is full.");
        var changes = week.Changes();

        var result = await changes.SwapAsync(Swap(), CancellationToken.None);

        Assert.False(result.Kept);
        Assert.Equal(FollowWords.SwapNotKept, result.Sentence);
        Assert.IsType<IOException>(result.Failure);
        Assert.Equal(0, week.Calendar.Edits);
        Assert.Equal(0, week.Calendar.FindCalls);
        Assert.False(changes.HasOwed);
    }

    [Fact]
    public async Task ASwapWithNothingToChangeIsNotKeptEither()
    {
        var week = new Week0();
        week.Source.Fails = new InvalidOperationException("There is no plan to change yet.");

        var result = await week.Changes().SwapAsync(Swap(), CancellationToken.None);

        Assert.False(result.Kept);
        Assert.Equal(FollowWords.SwapNotKept, result.Sentence);
    }

    [Fact]
    public async Task AnUnreachableCalendarIsSaidAndTheSwapIsKeptAndOwedARetry()
    {
        var week = new Week0();
        week.Calendar.Unavailable = true;
        var changes = week.Changes();

        var result = await changes.SwapAsync(Swap(), CancellationToken.None);

        Assert.True(result.Kept);
        Assert.Equal(FollowWords.CalendarUnchanged, result.Sentence);
        Assert.True(changes.HasOwed);
        Assert.Single(week.Source.Recorded);
        Assert.Equal(0, week.Calendar.Edits);
    }

    [Fact]
    public async Task TheRetryFollowsOnceTheCalendarAnswersAndTheSentenceGoes()
    {
        var week = new Week0();
        week.Calendar.Unavailable = true;
        var changes = week.Changes();
        var swap = Swap();
        await changes.SwapAsync(swap, CancellationToken.None);
        week.Calendar.Unavailable = false;

        var retry = await changes.RetryAsync(CancellationToken.None);

        Assert.Null(retry.Sentence);
        Assert.False(changes.HasOwed);
        Assert.Equal(swap.Moves.Single(move => move.Old.Session.Kind == SessionKind.Strength).New.Start, week.Calendar.ById(week.Strength).Start);
        Assert.Single(week.Source.Recorded);
    }

    [Fact]
    public async Task TheRetryGivesUpAfterTheThirdTryAndSaysSo()
    {
        var week = new Week0();
        week.Calendar.Unavailable = true;
        var changes = week.Changes();
        await changes.SwapAsync(Swap(), CancellationToken.None);

        var second = await changes.RetryAsync(CancellationToken.None);
        var third = await changes.RetryAsync(CancellationToken.None);

        Assert.Equal(FollowWords.CalendarUnchanged, second.Sentence);
        Assert.Equal(FollowWords.CalendarGaveUp, third.Sentence);
        Assert.False(changes.HasOwed);
        Assert.Equal(3, WorkoutChanges.MostTries);
    }

    [Fact]
    public async Task ASwapWhereOnlyOneEventMovedIsSaidToBeHalfDoneAndIsNotTriedAgain()
    {
        var week = new Week0();
        week.Door.Refuses.Add(week.Treadmill);
        var changes = week.Changes();

        var result = await changes.SwapAsync(Swap(), CancellationToken.None);

        Assert.True(result.Kept);
        Assert.Equal(FollowWords.CalendarPartlyChanged, result.Sentence);
        Assert.False(changes.HasOwed);
    }

    [Fact]
    public async Task AnEventTheOwnerMovedIsSaidToHaveBeenLeftWhereItWas()
    {
        var week = new Week0();
        week.Calendar.OwnerMoves(week.Strength, Est(11, 5, 19, 0), Est(11, 5, 19, 40));
        week.Calendar.OwnerMoves(week.Treadmill, Est(11, 7, 12, 0), Est(11, 7, 12, 30));
        var changes = week.Changes();

        var result = await changes.SwapAsync(Swap(), CancellationToken.None);

        Assert.True(result.Kept);
        Assert.Equal(FollowWords.CalendarLeftAlone, result.Sentence);
        Assert.False(changes.HasOwed);
    }

    [Fact]
    public async Task ACalendarThatFallsOverMidSwapIsNotTriedAgain()
    {
        var week = new Week0();
        week.Door.Throws = new HttpRequestException("503");
        var changes = week.Changes();

        var result = await changes.SwapAsync(Swap(), CancellationToken.None);

        Assert.True(result.Kept);
        Assert.Equal(FollowWords.CalendarPartlyChanged, result.Sentence);
        Assert.IsType<HttpRequestException>(result.Failure);
        Assert.False(changes.HasOwed);
    }

    [Fact]
    public async Task WithNoCalendarTheSwapIsJustKept()
    {
        var source = new Source();
        var changes = new WorkoutChanges(source, null);

        var result = await changes.SwapAsync(Swap(), CancellationToken.None);

        Assert.True(result.Kept);
        Assert.Null(result.Sentence);
        Assert.Single(source.Recorded);
    }

    // ---- a skip ----

    [Fact]
    public async Task ASkipIsRecordedAsTheSkipAloneThenTheEventIsTakenOff()
    {
        var week = new Week0();
        var changes = week.Changes();

        var result = await changes.SkipAsync(Skip(), CancellationToken.None);

        Assert.True(result.Kept);
        Assert.Null(result.Sentence);
        Assert.Equal(Thu, Assert.IsType<WorkoutsChange.SkipCommitted>(Assert.Single(week.Source.Recorded)).Skip.Day);
        Assert.DoesNotContain(week.Calendar.All, item => item.Id == week.Strength);
    }

    [Fact]
    public async Task ASkipThatCouldNotBeSavedIsNotKeptAndTheEventStays()
    {
        var week = new Week0();
        week.Source.Fails = new UnauthorizedAccessException("denied");
        var changes = week.Changes();

        var result = await changes.SkipAsync(Skip(), CancellationToken.None);

        Assert.False(result.Kept);
        Assert.Equal(FollowWords.SkipNotKept, result.Sentence);
        Assert.Contains(week.Calendar.All, item => item.Id == week.Strength);
        Assert.Equal(0, week.Calendar.FindCalls);
        Assert.False(changes.HasOwed);
    }

    [Fact]
    public async Task ASkipThatCouldNotBeSavedCanBeSavedLater()
    {
        var week = new Week0();
        week.Source.Fails = new IOException("The disk is full.");
        var changes = week.Changes();
        await changes.SkipAsync(Skip(), CancellationToken.None);
        week.Source.Fails = null;

        var again = await changes.SkipAsync(Skip(), CancellationToken.None);

        Assert.True(again.Kept);
        Assert.Single(week.Source.Recorded);
        Assert.DoesNotContain(week.Calendar.All, item => item.Id == week.Strength);
    }

    [Fact]
    public async Task AnEventThatWouldNotComeOffIsSaidAndTriedAgain()
    {
        var week = new Week0();
        week.Door.Refuses.Add(week.Strength);
        var changes = week.Changes();

        var first = await changes.SkipAsync(Skip(), CancellationToken.None);
        week.Door.Refuses.Clear();
        var retry = await changes.RetryAsync(CancellationToken.None);

        Assert.True(first.Kept);
        Assert.Equal(FollowWords.CalendarUnchanged, first.Sentence);
        Assert.Null(retry.Sentence);
        Assert.False(changes.HasOwed);
        Assert.DoesNotContain(week.Calendar.All, item => item.Id == week.Strength);
        Assert.Single(week.Source.Recorded);
    }

    [Fact]
    public async Task ASkippedSessionWithNoEventNeedsNoWords()
    {
        var week = new Week0();
        var changes = week.Changes();

        var result = await changes.SkipAsync(Skip() with { Key = null, OnCalendar = null }, CancellationToken.None);

        Assert.True(result.Kept);
        Assert.Null(result.Sentence);
        Assert.False(changes.HasOwed);
        Assert.Equal(0, week.Calendar.FindCalls);
    }

    [Fact]
    public async Task AnEventTheOwnerMovedIsNotRemovedAndTheTabSaysSo()
    {
        var week = new Week0();
        week.Calendar.OwnerMoves(week.Strength, Est(11, 5, 19, 0), Est(11, 5, 19, 40));

        var result = await week.Changes().SkipAsync(Skip(), CancellationToken.None);

        Assert.Equal(FollowWords.CalendarLeftAlone, result.Sentence);
        Assert.Contains(week.Calendar.All, item => item.Id == week.Strength);
    }

    // ---- nothing owed ----

    [Fact]
    public async Task ARetryWithNothingOwedDoesNothing()
    {
        var week = new Week0();
        var changes = week.Changes();

        var result = await changes.RetryAsync(CancellationToken.None);

        Assert.True(result.Kept);
        Assert.Null(result.Sentence);
        Assert.Equal(0, week.Calendar.FindCalls);
    }

    [Fact]
    public async Task ACancelledFollowIsCancelledNotSwallowed()
    {
        var week = new Week0();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => week.Changes().SwapAsync(Swap(), source.Token));
    }
}
