using Constitution.Domain.Notices;
using Constitution.Domain.Workouts.Screens;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.WorkoutsTab.WorkoutScreenFixture;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>
/// S2: a skip is not let go of when its undo window closes but when it has been written down. Closing the window used to clear the pending
/// skip on the spot, so a write that failed, or a tab that had not finished loading, lost it: the day came back unskipped on the next look.
/// </summary>
public class SkipRecordedTests
{
    private static readonly DateTimeOffset Start = Est(11, 3, 12);

    private static PlacedTime Placed() => new(Est(11, 5, 16, 0), Est(11, 5, 16, 40));

    private static SkipCommit? Begin(SkipUndo undo, NoticeBoard board, DateTimeOffset at, bool thursday = true) =>
        undo.Begin(board, Sam, thursday ? ThuStrength() : MonStrength(), Placed(), null, at);

    [Fact]
    public void ASkipWhoseWindowClosedIsStillShownUntilItIsRecorded()
    {
        var undo = new SkipUndo();
        Begin(undo, new NoticeBoard(), Start);

        var commit = undo.Due(Start.AddSeconds(6))!;

        Assert.Null(undo.Pending);
        Assert.Equal([Thu], undo.Shown.Select(skip => skip.Day));
        undo.Recorded(commit);
        Assert.Empty(undo.Shown);
    }

    [Fact]
    public void ASkipThatWasNotRecordedIsStillOwedAndIsNotHandedOutTwice()
    {
        var undo = new SkipUndo();
        Begin(undo, new NoticeBoard(), Start);
        undo.Due(Start.AddSeconds(6));

        Assert.Equal([Thu], undo.Unrecorded.Select(commit => commit.Skip.Day));
        Assert.Null(undo.Due(Start.AddSeconds(30)));
        Assert.Equal([Thu], undo.Unrecorded.Select(commit => commit.Skip.Day));
    }

    [Fact]
    public void ARecordedSkipIsOwedNoMore()
    {
        var undo = new SkipUndo();
        Begin(undo, new NoticeBoard(), Start);
        var commit = undo.Due(Start.AddSeconds(6))!;

        undo.Recorded(commit);

        Assert.Empty(undo.Unrecorded);
        Assert.Empty(undo.Shown);
    }

    [Fact]
    public void RecordingTwiceIsHarmless()
    {
        var undo = new SkipUndo();
        Begin(undo, new NoticeBoard(), Start);
        var commit = undo.Due(Start.AddSeconds(6))!;

        undo.Recorded(commit);
        undo.Recorded(commit);

        Assert.Empty(undo.Unrecorded);
    }

    [Fact]
    public void ASecondSkipLeavesTheFirstOwedUntilItIsRecorded()
    {
        var board = new NoticeBoard();
        var undo = new SkipUndo();
        Begin(undo, board, Start);
        var first = Begin(undo, board, Start.AddSeconds(2), thursday: false)!;

        Assert.Equal(Thu, first.Skip.Day);
        Assert.Equal([Thu, Mon], undo.Shown.Select(skip => skip.Day).Order());
        Assert.Equal([Thu], undo.Unrecorded.Select(commit => commit.Skip.Day));

        undo.Recorded(first);

        Assert.Equal([Mon], undo.Shown.Select(skip => skip.Day));
        Assert.Empty(undo.Unrecorded);
    }

    [Fact]
    public void RecordingAnotherSkipChangesNothing()
    {
        var board = new NoticeBoard();
        var undo = new SkipUndo();
        Begin(undo, board, Start);
        var commit = undo.Due(Start.AddSeconds(6))!;
        Begin(undo, board, Start.AddSeconds(10), thursday: false);

        undo.Recorded(new SkipCommit(undo.Pending!));

        Assert.Equal([Thu], undo.Unrecorded.Select(owed => owed.Skip.Day));
        Assert.Equal(Thu, commit.Skip.Day);
    }

    [Fact]
    public void AnUndoneSkipIsNeitherShownNorOwed()
    {
        var board = new NoticeBoard();
        var undo = new SkipUndo();
        Begin(undo, board, Start);

        undo.Reverse(board.TurnUndo(Start.AddSeconds(2))!);

        Assert.Empty(undo.Shown);
        Assert.Empty(undo.Unrecorded);
    }

    [Fact]
    public void ASkipWithinItsWindowIsShownButNotOwed()
    {
        var undo = new SkipUndo();
        Begin(undo, new NoticeBoard(), Start);

        Assert.Equal([Thu], undo.Shown.Select(skip => skip.Day));
        Assert.Empty(undo.Unrecorded);
    }

    [Fact]
    public void EverySkipOnShowIsStruckThroughOnTheTab()
    {
        var model = WorkoutsTabModel.Build(Snapshot(), [new PendingSkip(Sam, Thu, Constitution.Domain.Workouts.SessionKind.Strength, ThuStrength(), null, null, Start), new PendingSkip(Sam, Mon, Constitution.Domain.Workouts.SessionKind.Strength, MonStrength(), null, null, Start)]);

        Assert.True(model.Days.Single(day => day.Day == Thu).IsSkipped);
        Assert.True(model.Days.Single(day => day.Day == Mon).IsSkipped);
        Assert.False(model.Days.Single(day => day.Day == Sat).IsSkipped);
    }

    [Fact]
    public void NoSkipOnShowStrikesNothingThrough()
    {
        var model = WorkoutsTabModel.Build(Snapshot(), Array.Empty<PendingSkip>());

        Assert.All(model.Days, day => Assert.False(day.IsSkipped));
    }
}
