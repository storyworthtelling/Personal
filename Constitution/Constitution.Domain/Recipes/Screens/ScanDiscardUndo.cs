using Constitution.Domain.Notices;

namespace Constitution.Domain.Recipes.Screens;

/// <summary>
/// Letting go of a draft, undo-first (DESIGN.md section 5; the same shape as <see cref="CuisineMoveUndo"/>). When a new scan replaces
/// a draft the owner had, the old one is not thrown away at once: it waits out the shared dog-ear's window, a turn of the fold
/// hands it back, and when the window closes its words are forgotten. No question is asked. A second discard while one waits makes the
/// first permanent. Time is passed in.
/// </summary>
public sealed class ScanDiscardUndo
{
    public const string ActKey = "recipe-scan:discard";

    private DateTimeOffset expires;

    /// <summary>The draft inside its undo window, if there is one.</summary>
    public ScanConfirmModel? Pending { get; private set; }

    public void Begin(NoticeBoard board, ScanConfirmModel draft, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(draft);
        if (Pending is { } earlier && !ReferenceEquals(earlier, draft))
        {
            earlier.Discard();
        }

        Pending = draft;
        expires = now + NoticeBoard.UndoWindow;
        board.BeginUndo(ActKey, now);
    }

    /// <summary>The fold was turned and the board gave back this act key: the draft to bring back, or null when the act was not ours.</summary>
    public ScanConfirmModel? Reverse(string actKey)
    {
        if (Pending is not { } waiting || actKey != ActKey)
        {
            return null;
        }

        Pending = null;
        return waiting;
    }

    /// <summary>The window has closed: the draft's words are forgotten. Returns whether there was one to let go.</summary>
    public bool Expire(DateTimeOffset now)
    {
        if (Pending is not { } waiting || now < expires)
        {
            return false;
        }

        waiting.Discard();
        Pending = null;
        return true;
    }
}
