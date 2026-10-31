using Constitution.Domain.Household;
using Constitution.Domain.Notices;
using Constitution.Tests.HouseholdFile;

namespace Constitution.Tests.PersonSheet;

public class PersonRemovalUndoTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    private static RemovedPerson Removed(PersonId id)
    {
        var document = SheetData.Full();
        return RosterRemoval.Remove(new RosterEditor(RosterRole.Owner), document, id, SheetData.After(document), RosterData.OwnerWriter).Removed!;
    }

    [Fact]
    public void BeginFoldsTheCornerForTheWindow()
    {
        var board = new NoticeBoard();
        var undo = new PersonRemovalUndo();

        undo.Begin(board, Removed(SheetData.Robin), T0);

        Assert.True(board.Fold(T0.AddSeconds(1))!.IsUndo);
        Assert.Null(board.Fold(T0 + NoticeBoard.UndoWindow + TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void TurningTheFoldInsideTheWindowHandsBackThePerson()
    {
        var undo = new PersonRemovalUndo();
        var robin = Removed(SheetData.Robin);
        undo.Begin(new NoticeBoard(), robin, T0);

        var back = undo.Reverse(PersonRemovalUndo.ActKey(SheetData.Robin), T0.AddSeconds(2));

        Assert.Same(robin, back);
        Assert.Null(undo.Pending);
        Assert.Null(undo.Reverse(PersonRemovalUndo.ActKey(SheetData.Robin), T0.AddSeconds(3)));
    }

    [Fact]
    public void AfterTheWindowTheRemovalStands()
    {
        var undo = new PersonRemovalUndo();
        undo.Begin(new NoticeBoard(), Removed(SheetData.Robin), T0);
        var late = T0 + NoticeBoard.UndoWindow + TimeSpan.FromMilliseconds(1);

        Assert.Null(undo.Reverse(PersonRemovalUndo.ActKey(SheetData.Robin), late));
        Assert.True(undo.Expire(late));
        Assert.Null(undo.Pending);
    }

    [Fact]
    public void ExpiryWaitsForTheWindow()
    {
        var undo = new PersonRemovalUndo();
        undo.Begin(new NoticeBoard(), Removed(SheetData.Robin), T0);

        Assert.False(undo.Expire(T0.AddSeconds(1)));
        Assert.NotNull(undo.Pending);
    }

    [Fact]
    public void AnotherActsKeyLeavesItWaiting()
    {
        var undo = new PersonRemovalUndo();
        undo.Begin(new NoticeBoard(), Removed(SheetData.Robin), T0);

        Assert.Null(undo.Reverse("recipe-move:tacos", T0.AddSeconds(1)));
        Assert.NotNull(undo.Pending);
    }

    [Fact]
    public void ASecondRemovalIsRefusedWhileTheFirstCanStillBeUndone()
    {
        var board = new NoticeBoard();
        var undo = new PersonRemovalUndo();
        var robin = Removed(SheetData.Robin);
        undo.Begin(board, robin, T0);

        var refusal = undo.RefusalFor(T0.AddSeconds(1));

        Assert.Equal("Robin was just removed. Turn the corner to bring them back, or wait a moment, before removing someone else.", refusal);
        Assert.Throws<InvalidOperationException>(() => undo.Begin(board, Removed(SheetData.Juniper), T0.AddSeconds(1)));
        Assert.Same(robin, undo.Reverse(PersonRemovalUndo.ActKey(SheetData.Robin), T0.AddSeconds(2)));
    }

    [Fact]
    public void ASecondRemovalIsAllowedOnceTheWindowHasClosed()
    {
        var board = new NoticeBoard();
        var undo = new PersonRemovalUndo();
        undo.Begin(board, Removed(SheetData.Robin), T0);
        var late = T0 + NoticeBoard.UndoWindow + TimeSpan.FromMilliseconds(1);

        Assert.Null(undo.RefusalFor(late));
        undo.Begin(board, Removed(SheetData.Juniper), late);

        Assert.Equal("juniper", undo.Pending!.Id.Value);
    }

    [Fact]
    public void NothingPendingRefusesNothing()
    {
        Assert.Null(new PersonRemovalUndo().RefusalFor(T0));
    }

    [Fact]
    public void AnUndoneRemovalLeavesTheNextFree()
    {
        var undo = new PersonRemovalUndo();
        undo.Begin(new NoticeBoard(), Removed(SheetData.Robin), T0);
        undo.Reverse(PersonRemovalUndo.ActKey(SheetData.Robin), T0.AddSeconds(1));

        Assert.Null(undo.RefusalFor(T0.AddSeconds(2)));
    }

    [Fact]
    public void TheSentencesNameThePersonAndNoDate()
    {
        var robin = Removed(SheetData.Robin);

        Assert.Equal("Robin is off the roster. Turn the corner to bring them back.", PersonRemovalUndo.RemovedSentence(robin));
        Assert.Equal("Robin is back on the roster.", PersonRemovalUndo.RestoredSentence(robin));
        Assert.Equal("Robin was removed from the roster.", PersonRemovalUndo.StandsSentence(robin));
    }
}
