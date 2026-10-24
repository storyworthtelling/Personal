using Constitution.Domain.Notices;

namespace Constitution.Domain.Recipes.Screens;

/// <summary>
/// Moving a card's cuisine, undo-first (C-6.2.4, D64; DESIGN.md section 5): the move happens at once and folds the shared
/// dog-ear for the undo window; a turn of the fold inside it hands back the move's inverse
/// (<see cref="CuisineMove.Inverse"/>) for the caller to apply the way it applied the move. No confirm dialog. Time is passed in.
/// A move that begins while another waits makes the first permanent, as the board does for any act.
/// </summary>
public sealed class CuisineMoveUndo
{
    public const string NotKeptSentence = "That card could not be moved, so it was left where it was.";
    public const string NotUndoneSentence = "That move could not be undone. Move the card back by hand.";

    private DateTimeOffset expires;

    /// <summary>The move inside its undo window, if there is one.</summary>
    public CuisineMove? Pending { get; private set; }

    public static string ActKey(RecipeId card) => "recipe-move:" + card.Value;

    /// <summary>Folds the corner for this move. The move itself has been, or is being, applied by the caller.</summary>
    public void Begin(NoticeBoard board, CuisineMove move, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(move);
        Pending = move;
        expires = now + NoticeBoard.UndoWindow;
        board.BeginUndo(ActKey(move.Card), now);
    }

    /// <summary>The fold was turned and the board gave back this act key: the inverse to apply, or null when it was not ours.</summary>
    public CuisineMove? Reverse(string actKey)
    {
        if (Pending is not { } waiting || actKey != ActKey(waiting.Card))
        {
            return null;
        }

        Pending = null;
        return waiting.Inverse;
    }

    /// <summary>The window has closed: the move is let go of. Returns whether there was one to let go.</summary>
    public bool Expire(DateTimeOffset now)
    {
        if (Pending is null || now < expires)
        {
            return false;
        }

        Pending = null;
        return true;
    }

    /// <summary>The cards as they should show while the move waits: the pending move already applied to a snapshot read before it.</summary>
    public IReadOnlyList<Recipe> Show(IReadOnlyList<Recipe> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        return Pending is not { } move
            ? cards
            : cards.Select(card => card.Id == move.Card ? card with { Cuisine = move.To } : card).ToList();
    }
}
