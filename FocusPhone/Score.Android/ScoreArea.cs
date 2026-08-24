using Android.App;
using Android.Views;

namespace Score;

/// <summary>
/// One of Score's top-level areas — what will sit behind one binder tab.
///
/// An area owns its own state and builds its own pane; the shell owns the frame, the
/// now-playing bar and which area is showing. The two rules that keep that split
/// honest are here: an area asks to be redrawn by raising <see cref="Changed"/>
/// rather than calling the shell, and it answers Back for itself, because only the
/// area knows whether it is drilled into something.
/// </summary>
public abstract class ScoreArea(Activity host)
{
    protected Activity Host { get; } = host;

    /// <summary>The area's content, built fresh on every render.</summary>
    public abstract View BuildView();

    /// <summary>
    /// Back, offered to the area first. Return true when it was used to come up a
    /// level; false lets the press leave the app.
    /// </summary>
    public virtual bool GoBack() => false;

    /// <summary>
    /// Raised when the area's content is stale. The shell redraws only if this area
    /// is the one showing — a background scan finishing behind another tab should
    /// not rebuild that tab.
    /// </summary>
    public event Action? Changed;

    protected void Invalidate() => Changed?.Invoke();
}
