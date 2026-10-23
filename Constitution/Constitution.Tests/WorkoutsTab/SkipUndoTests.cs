using Constitution.Domain.Notices;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Screens;
using Constitution.Tests.Placement;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.WorkoutsTab.WorkoutScreenFixture;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>C-5.4.4 (book p.12, p.18; D77): skip at once, six seconds to undo, nothing recorded or removed until the window closes. Invented data.</summary>
public class SkipUndoTests
{
    private static readonly DateTimeOffset Start = Est(11, 3, 12);

    private static PlacementKey Key() => PlacementKey.ForWorkout(Sam, SessionKind.Strength, PlacementKey.WeekIndexOf(Tue), 1);

    private static PlacedTime Placed() => new(Est(11, 5, 16, 0), Est(11, 5, 16, 40));

    private static SkipCommit? Begin(SkipUndo undo, NoticeBoard board, DateTimeOffset at) =>
        undo.Begin(board, Sam, ThuStrength(), Placed(), Key(), at);

    [Fact]
    public void RestoresWithinTheWindow()
    {
        var board = new NoticeBoard();
        var undo = new SkipUndo();
        Begin(undo, board, Start);

        var act = board.TurnUndo(Start.AddSeconds(5.9));
        var restored = undo.Reverse(act!);

        Assert.Equal(Thu, restored!.Day);
        Assert.Null(undo.Pending);
        Assert.Null(undo.Due(Start.AddSeconds(7)));
    }

    [Fact]
    public void ATurnAfterTheWindowRestoresNothing()
    {
        var board = new NoticeBoard();
        var undo = new SkipUndo();
        Begin(undo, board, Start);

        Assert.Null(board.TurnUndo(Start.AddSeconds(6)));
        Assert.NotNull(undo.Due(Start.AddSeconds(6)));
    }

    [Fact]
    public async Task RemovesTheEventAfterIt()
    {
        var calendar = new FakeHeldCalendar();
        var store = new MemoryKeyStore();
        var id = calendar.Add(Sam, Key(), Placed().Start, Placed().End);
        store.Save(WrittenKeys.Empty.With(Key()));
        var follow = new WorkoutCalendarFollow(calendar, calendar, store);
        var board = new NoticeBoard();
        var undo = new SkipUndo();
        Begin(undo, board, Start);

        Assert.Null(undo.Due(Start.AddSeconds(5.9)));
        Assert.Contains(calendar.All, item => item.Id == id);

        var commit = undo.Due(Start.AddSeconds(6));
        Assert.NotNull(commit);
        var outcome = await follow.FollowSkipAsync(commit.Skip, CancellationToken.None);

        Assert.Equal(FollowOutcome.Removed, outcome);
        Assert.Empty(calendar.All);
        Assert.Empty(board.Waiting());
        Assert.Null(undo.Due(Start.AddSeconds(8)));
    }

    [Fact]
    public async Task AnEventTheOwnerMovedIsNotRemoved()
    {
        var calendar = new FakeHeldCalendar();
        var store = new MemoryKeyStore();
        var id = calendar.Add(Sam, Key(), Est(11, 5, 19, 0), Est(11, 5, 19, 40));
        store.Save(WrittenKeys.Empty.With(Key()));
        var undo = new SkipUndo();
        Begin(undo, new NoticeBoard(), Start);

        var outcome = await new WorkoutCalendarFollow(calendar, calendar, store).FollowSkipAsync(undo.Due(Start.AddSeconds(6))!.Skip, CancellationToken.None);

        Assert.Equal(FollowOutcome.OwnerHadMovedIt, outcome);
        Assert.Contains(calendar.All, item => item.Id == id);
    }

    [Fact]
    public async Task ASkippedSessionWithNoEventNeedsNoCalendarWork()
    {
        var undo = new SkipUndo();
        undo.Begin(new NoticeBoard(), Sam, ThuStrength(), null, null, Start);
        var calendar = new FakeHeldCalendar();

        var outcome = await new WorkoutCalendarFollow(calendar, calendar, new MemoryKeyStore()).FollowSkipAsync(undo.Due(Start.AddSeconds(6))!.Skip, CancellationToken.None);

        Assert.Equal(FollowOutcome.NotOnCalendar, outcome);
        Assert.Equal(0, calendar.FindCalls);
    }

    [Fact]
    public void UndoOutranksNotices()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.BedOffline, "bed", Start.AddMinutes(-5));
        var undo = new SkipUndo();
        Assert.False(board.Fold(Start)!.IsUndo);

        Begin(undo, board, Start);

        Assert.True(board.Fold(Start.AddSeconds(3))!.IsUndo);
        Assert.Equal(NoticeBoard.UndoRole, board.Fold(Start.AddSeconds(3))!.Role);
        Assert.False(board.Fold(Start.AddSeconds(6.1))!.IsUndo);
        Assert.Single(board.Waiting());
    }

    [Fact]
    public void ACommittedSkipIsRecordedAsASkip()
    {
        var undo = new SkipUndo();
        Begin(undo, new NoticeBoard(), Start);

        var ledger = undo.Due(Start.AddSeconds(6))!.Record(SkipLedger.Empty);

        Assert.Equal(SessionOutcome.Skipped, ledger.OutcomeOf(Sam, Thu));
    }

    [Fact]
    public void AnUndoneSkipIsNeverRecorded()
    {
        var board = new NoticeBoard();
        var undo = new SkipUndo();
        Begin(undo, board, Start);
        undo.Reverse(board.TurnUndo(Start.AddSeconds(2))!);

        Assert.Null(undo.Due(Start.AddSeconds(20)));
    }

    [Fact]
    public void ASecondSkipMakesTheFirstPermanent()
    {
        var board = new NoticeBoard();
        var undo = new SkipUndo();
        Begin(undo, board, Start);

        var first = undo.Begin(board, Sam, MonStrength(), null, null, Start.AddSeconds(2));

        Assert.Equal(Thu, first!.Skip.Day);
        Assert.Equal(Mon, undo.Pending!.Day);
        Assert.Null(undo.Reverse(SkipUndo.ActKey(Thu)));
    }

    [Fact]
    public void TheWindowIsSixSeconds()
    {
        var undo = new SkipUndo();
        Begin(undo, new NoticeBoard(), Start);

        Assert.Equal(Start.AddSeconds(6), undo.Pending!.Expires);
    }
}
