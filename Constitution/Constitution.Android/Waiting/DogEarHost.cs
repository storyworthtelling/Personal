using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Ui;
using Constitution.Domain.Notices;
using Constitution.Settings;

namespace Constitution.Waiting;

/// <summary>
/// Puts the one fold in the corner of whichever page is showing (book p.18; DESIGN.md §13): flush to
/// the top-right of the overlay, which wraps the page and stops at the binder gutter. What it wears
/// is <see cref="NoticeBoard.Fold"/>'s call and nothing here decides it — the most serious notice's
/// hue, or the Action pine with the back-turn arrow while an undo is open. No count. A notice fold
/// turns to the Waiting page; an undo fold turns the act back.
///
/// <para>The board is the process-wide <see cref="ConnectionNoticeBoard"/>, the one Settings already
/// raises into; there is no second board. The host holds no state of its own beyond the view, so
/// <see cref="Refresh"/> is safe to call on every render, and it schedules its own flattening when
/// an undo's window closes.</para>
/// </summary>
internal sealed class DogEarHost(Context context, FrameLayout overlay)
{
    private static readonly Java.Lang.String FoldTag = new("constitution-dog-ear");

    private readonly Handler handler = new(Looper.MainLooper!);

    /// <summary>Raised when a notice fold is turned: the shell opens the Waiting page.</summary>
    public event Action? OpenWaiting;

    /// <summary>Raised with the act key when an undo fold is turned, for the shell to reverse that act.</summary>
    public event Action<string>? UndoTurned;

    /// <summary>Repaints the corner for the board as it stands now.</summary>
    public void Refresh()
    {
        handler.RemoveCallbacksAndMessages(null);
        if (overlay.FindViewWithTag(FoldTag) is View existing)
        {
            overlay.RemoveView(existing);
        }

        var now = DateTimeOffset.UtcNow;
        var fold = ConnectionNoticeBoard.With(board => board.Fold(now));
        if (fold is null)
        {
            return;
        }

        var hint = fold.IsUndo ? "Undo" : "Something is waiting";
        var view = new FoldView(context, hint, fold.Role, showsBackArrow: fold.IsUndo) { Tag = FoldTag };
        view.Click += (_, _) => Turn();

        var size = PaperTheme.Dp(context, Codex.Domain.Design.DogEarGeometry.SizeDp);
        overlay.AddView(view, new FrameLayout.LayoutParams(size, size, GravityFlags.Top | GravityFlags.End));

        if (fold.Expires is { } expires)
        {
            // The undo's window closes by itself; the notices it outranked come back then.
            var wait = (long)Math.Max(0, (expires - now).TotalMilliseconds) + 1;
            handler.PostDelayed(Refresh, wait);
        }
    }

    /// <summary>Stops the pending flatten; call when the shell pauses.</summary>
    public void Pause() => handler.RemoveCallbacksAndMessages(null);

    private void Turn()
    {
        var now = DateTimeOffset.UtcNow;
        var fold = ConnectionNoticeBoard.With(board => board.Fold(now));
        if (fold is null)
        {
            Refresh();
            return;
        }

        if (!fold.IsUndo)
        {
            OpenWaiting?.Invoke();
            return;
        }

        var act = ConnectionNoticeBoard.With(board => board.TurnUndo(now));
        if (act is not null)
        {
            UndoTurned?.Invoke(act);
        }

        Refresh();
    }
}
