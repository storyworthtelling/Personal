using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Ui;

namespace Plate;

/// <summary>
/// One of Plate's top-level areas — what sits behind one binder tab.
///
/// <para>The split is Score's, adopted by Post and now by Plate: an area owns its own state
/// and builds its own pane, while the shell owns the frame and which area is showing. Two
/// rules keep that honest — an area asks to be redrawn by raising <see cref="Changed"/>
/// rather than reaching into the shell, and it answers Back for itself, because only the
/// area knows whether it is drilled into something.</para>
///
/// <para>This is the third head to carry its own copy of that base class, and the three
/// have drifted apart rather than together: Score's carries a now-playing bar and a
/// one-second tick, Post's a permissions callback. Unifying them is worth doing and is
/// deliberately not done here — it would put two shipping heads at risk in the middle of a
/// release, for no behaviour the owner would see.</para>
/// </summary>
public abstract class PlateArea(Activity host)
{
    protected Activity Host { get; } = host;

    /// <summary>
    /// The frame the shell draws content in, and the one a dog-ear overlays. Handed over
    /// once the shell has built its layout, which is why this is set rather than injected —
    /// the areas exist before the frame does.
    /// </summary>
    public FrameLayout Overlay { get; set; } = null!;

    /// <summary>
    /// How this area appears in the binder gutter (DESIGN.md §9): a short label, a glyph,
    /// the hue it lights in and the cloth its tab is cut from. The area declares its own
    /// tab so the shell never carries a table of who is who.
    /// </summary>
    public abstract BinderArea Tab { get; }

    /// <summary>The in-page Serif header (§7). Usually the area's own name.</summary>
    public virtual string Title => Tab.Label;

    /// <summary>The area's content, built fresh on every render.</summary>
    public abstract View BuildView();

    /// <summary>
    /// Back, offered to the area first. Return true when it was used to come up a level;
    /// false lets the press leave the app.
    /// </summary>
    public virtual bool GoBack() => false;

    /// <summary>
    /// The area's own actions, hosted by the shell in a pinned bottom bar. Null when the
    /// area has none.
    /// </summary>
    public virtual View? BuildBottomBar() => null;

    /// <summary>
    /// The screen came back. Called on <b>every</b> area, showing or not — the camera has
    /// to release its hold on the device when another area is looking at it, and cannot do
    /// that if it is only told about its own tab.
    /// </summary>
    public virtual void Resume()
    {
    }

    /// <summary>The screen is going away. Called on every area, as Resume is.</summary>
    public virtual void Pause()
    {
    }

    /// <summary>
    /// This area became the one showing, or stopped being it. The camera binds and unbinds
    /// on this: holding the sensor open behind the Settings tab drains the battery and
    /// blocks every other app that wants a camera.
    /// </summary>
    public virtual void OnShown(bool showing)
    {
    }

    /// <summary>An activity result the shell could not place. Areas claim their own.</summary>
    public virtual void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
    }

    /// <summary>A permissions answer. Areas that asked for something react to it.</summary>
    public virtual void OnPermissionsResult()
    {
    }

    /// <summary>
    /// Raised when the area's content is stale. The shell redraws only if this area is the
    /// one showing.
    /// </summary>
    public event Action? Changed;

    protected void Invalidate() => Changed?.Invoke();
}
