using Constitution.Domain.Notices;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// A draft the owner scans over is let go undo-first (DESIGN.md section 5): it waits out the shared dog-ear's window, a turn of the
/// fold hands it back, and when the window closes its words are forgotten. Nothing is asked.
/// </summary>
public class ScanDiscardUndoTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 18, 0, 0, TimeSpan.Zero);

    private static ScanConfirmModel Draft() => ScanConfirmModel.From(ScanSamples.AppleCrisp());

    [Fact]
    public void TheFoldHandsTheDraftBack()
    {
        var board = new NoticeBoard();
        var undo = new ScanDiscardUndo();
        var parked = Draft();
        undo.Begin(board, parked, Now);

        var act = board.TurnUndo(Now.AddSeconds(2));
        var back = undo.Reverse(act!);

        Assert.Same(parked, back);
        Assert.Null(undo.Pending);
        Assert.Equal("Apple crisp", back!.Title);
    }

    [Fact]
    public void BeginningFoldsTheSharedCornerForTheWindow()
    {
        var board = new NoticeBoard();
        new ScanDiscardUndo().Begin(board, Draft(), Now);

        Assert.NotNull(board.Fold(Now.AddSeconds(1)));
        Assert.Null(board.TurnUndo(Now + NoticeBoard.UndoWindow + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AnotherActsKeyReturnsNothing()
    {
        var undo = new ScanDiscardUndo();
        undo.Begin(new NoticeBoard(), Draft(), Now);

        Assert.Null(undo.Reverse("recipe-move:black-bean-chili"));
        Assert.NotNull(undo.Pending);
    }

    [Fact]
    public void WhenTheWindowClosesTheWordsAreForgotten()
    {
        var undo = new ScanDiscardUndo();
        var parked = Draft();
        undo.Begin(new NoticeBoard(), parked, Now);

        Assert.False(undo.Expire(Now.AddSeconds(1)));
        Assert.True(undo.Expire(Now + NoticeBoard.UndoWindow + TimeSpan.FromSeconds(1)));

        Assert.True(parked.IsDiscarded);
        Assert.Null(undo.Pending);
    }

    [Fact]
    public void ANewDiscardMakesTheOldOnePermanent()
    {
        var board = new NoticeBoard();
        var undo = new ScanDiscardUndo();
        var first = Draft();
        var second = Draft();
        undo.Begin(board, first, Now);
        undo.Begin(board, second, Now.AddSeconds(1));

        Assert.True(first.IsDiscarded);
        Assert.Same(second, undo.Reverse(ScanDiscardUndo.ActKey));
    }
}
