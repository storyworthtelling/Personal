using Android.App;
using Android.Content;
using Android.Views;
using Codex.Ui;

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

    /// <summary>
    /// How this area appears in the binder gutter (DESIGN.md §9): a short label, an
    /// engraved glyph, and the hue it lights in. The area declares its own tab so the
    /// shell never carries a table of who is who.
    /// </summary>
    public abstract BinderArea Tab { get; }

    /// <summary>
    /// The in-page title (§7). Usually the area's own name, but not always — Playing
    /// is titled for the area, not for the track.
    /// </summary>
    public virtual string Title => Tab.Label;

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
    /// Whether the owner's voice belongs on this screen. False everywhere by default:
    /// the voice sits on the two <b>roots</b> that are about the owner's library —
    /// Music and Podcasts — and stays out of the three areas that are a tool, a mode
    /// and a control panel. It also leaves as soon as you drill in, or the line would
    /// be repeated down every level of the library.
    ///
    /// This is about the hourly seat, not about voiced copy in general: the crate
    /// writes a note per record and the workout writes one per track, which are
    /// per-item and belong to their screens.
    /// </summary>
    public virtual bool ShowsVoice => false;

    /// <summary>
    /// Whether the shell's now-playing bar belongs on this area. False wherever the
    /// screen already shows the current track — repeating it in a bar underneath is
    /// the same information twice.
    /// </summary>
    public virtual bool ShowsNowPlayingBar => true;

    /// <summary>
    /// The shell's one-second tick. An area that shows live playback updates its own
    /// views here rather than raising <see cref="Changed"/>, which would rebuild the
    /// pane out from under a scrubber someone is dragging.
    /// </summary>
    public virtual void OnTick()
    {
    }

    /// <summary>An activity result the shell could not place. Areas claim their own.</summary>
    public virtual void OnActivityResult(int requestCode, Result resultCode, Intent? data)
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
