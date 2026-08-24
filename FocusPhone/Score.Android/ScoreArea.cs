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

    /// <summary>
    /// The frame the shell draws the content in, and the one an undo bar or an action
    /// card overlays. Handed over once the shell has built its layout, which is why
    /// this is set rather than injected — the areas exist before the frame does.
    /// </summary>
    public FrameLayout Overlay { get; set; } = null!;

    /// <summary>The area's content, built fresh on every render.</summary>
    public abstract View BuildView();

    /// <summary>
    /// Back, offered to the area first. Return true when it was used to come up a
    /// level; false lets the press leave the app.
    /// </summary>
    public virtual bool GoBack() => false;

    /// <summary>
    /// The area's own actions, hosted by the shell beneath the now-playing bar. Null
    /// when the area has none, which is how the bar disappears on the music side
    /// instead of being hidden by the shell knowing whose bar it is.
    /// </summary>
    public virtual View? BuildBottomBar() => null;

    /// <summary>
    /// The screen came back. Called on <b>every</b> area, showing or not: a feed
    /// refresh that only ran on the tab you happened to be looking at would leave
    /// the other one stale exactly when you switched to it.
    /// </summary>
    public virtual void Resume()
    {
    }

    /// <summary>The screen is going away. Called on every area, as Resume is.</summary>
    public virtual void Pause()
    {
    }

    /// <summary>
    /// Raised when the area's content is stale. The shell redraws only if this area
    /// is the one showing — a background scan finishing behind another tab should
    /// not rebuild that tab.
    /// </summary>
    public event Action? Changed;

    protected void Invalidate() => Changed?.Invoke();
}
