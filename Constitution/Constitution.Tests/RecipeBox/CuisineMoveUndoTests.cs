using Constitution.Domain.Notices;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeBoxScreens;

/// <summary>Moving a card's cuisine, undo-first (C-6.2.4, D64, DESIGN.md section 5): the move happens, the fold turns it back.</summary>
public class CuisineMoveUndoTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 18, 0, 0, TimeSpan.Zero);
    private static readonly RecipeId Chili = new("black-bean-chili");

    private static CuisineMove Move() => new(Chili, Cuisine.Mexican, Cuisine.Other);

    [Fact]
    public void MoveIsUndoable()
    {
        var board = new NoticeBoard();
        var undo = new CuisineMoveUndo();
        undo.Begin(board, Move(), Now);

        var act = board.TurnUndo(Now.AddSeconds(2));
        var inverse = undo.Reverse(act!);

        Assert.Equal(Move().Inverse, inverse);
        Assert.Null(undo.Pending);
    }

    [Fact]
    public void BeginningFoldsTheSharedCornerForTheUndoWindow()
    {
        var board = new NoticeBoard();
        new CuisineMoveUndo().Begin(board, Move(), Now);

        Assert.NotNull(board.Fold(Now.AddSeconds(1)));
        Assert.Null(board.TurnUndo(Now + NoticeBoard.UndoWindow + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AnotherActsKeyReversesNothing()
    {
        var undo = new CuisineMoveUndo();
        undo.Begin(new NoticeBoard(), Move(), Now);

        Assert.Null(undo.Reverse("workout-skip:2026-10-24"));
        Assert.NotNull(undo.Pending);
    }

    [Fact]
    public void ANewMoveMakesTheOldOnePermanent()
    {
        var board = new NoticeBoard();
        var undo = new CuisineMoveUndo();
        var other = new CuisineMove(new RecipeId("lemon-orzo"), Cuisine.Italian, Cuisine.Other);
        undo.Begin(board, Move(), Now);
        undo.Begin(board, other, Now.AddSeconds(1));

        Assert.Null(undo.Reverse(CuisineMoveUndo.ActKey(Chili)));
        Assert.Equal(other.Inverse, undo.Reverse(CuisineMoveUndo.ActKey(other.Card)));
    }

    [Fact]
    public void WhenTheWindowClosesTheMoveIsLetGo()
    {
        var undo = new CuisineMoveUndo();
        undo.Begin(new NoticeBoard(), Move(), Now);

        Assert.False(undo.Expire(Now.AddSeconds(1)));
        Assert.NotNull(undo.Pending);
        Assert.True(undo.Expire(Now + NoticeBoard.UndoWindow));
        Assert.Null(undo.Pending);
    }

    [Fact]
    public void MovingAndUndoingThroughTheSourceRestoresTheCuisine()
    {
        var source = new InMemoryBoxSource(BoxFixture.Twelve());
        var undo = new CuisineMoveUndo();
        var board = new NoticeBoard();
        var move = new RecipeBox(BoxFixture.Twelve()).Move(BoxFixture.Chili(), Cuisine.Other)!;

        source.Move(move);
        undo.Begin(board, move, Now);
        source.Move(undo.Reverse(board.TurnUndo(Now.AddSeconds(1))!)!);

        var ready = (RecipeBoxRead.Ready)source.Read();
        Assert.Equal(Cuisine.Mexican, ready.Snapshot.Cards.Single(card => card.Id == Chili).Cuisine);
    }

    [Fact]
    public void TheAppliedMoveShowsOnTheCardsAtOnce()
    {
        var undo = new CuisineMoveUndo();
        undo.Begin(new NoticeBoard(), Move(), Now);

        var shown = undo.Show(BoxFixture.Twelve());

        Assert.Equal(Cuisine.Other, shown.Single(card => card.Id == Chili).Cuisine);
        Assert.Equal(12, shown.Count);
    }
}
