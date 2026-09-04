using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Ui;

namespace Post;

/// <summary>
/// One of Post's top-level areas — what sits behind one binder tab.
///
/// The split is Score's, and it is what breaks up the 649-line list screen this head
/// used to be: an area owns its own state and builds its own pane, while the shell owns
/// the frame and which area is showing. Two rules keep that honest — an area asks to be
/// redrawn by raising <see cref="Changed"/> rather than reaching into the shell, and it
/// answers Back for itself, because only the area knows whether it is drilled into
/// something.
/// </summary>
public abstract class PostArea(Activity host)
{
    protected Activity Host { get; } = host;

    /// <summary>
    /// The frame the shell draws content in, and the one a dog-ear or an action card
    /// overlays. Handed over once the shell has built its layout, which is why this is
    /// set rather than injected — the areas exist before the frame does.
    /// </summary>
    public FrameLayout Overlay { get; set; } = null!;

    /// <summary>
    /// How this area appears in the binder gutter (DESIGN.md §9): a short label, a
    /// glyph, the hue it lights in and the cloth its tab is cut from. The area declares
    /// its own tab so the shell never carries a table of who is who.
    /// </summary>
    public abstract BinderArea Tab { get; }

    /// <summary>The in-page Serif header (§7). Usually the area's own name.</summary>
    public virtual string Title => Tab.Label;

    /// <summary>The area's content, built fresh on every render.</summary>
    public abstract View BuildView();

    /// <summary>
    /// Back, offered to the area first. Return true when it was used to come up a
    /// level; false lets the press leave the app.
    /// </summary>
    public virtual bool GoBack() => false;

    /// <summary>
    /// The area's own actions, hosted by the shell in a pinned bottom bar. Null when the
    /// area has none — the Mailbox has none by design, because its acts are per-row.
    /// </summary>
    public virtual View? BuildBottomBar() => null;

    /// <summary>
    /// The screen came back. Called on <b>every</b> area, showing or not: a sync that
    /// only ran on the tab you happened to be looking at would leave the other stale
    /// exactly when you switched to it.
    /// </summary>
    public virtual void Resume()
    {
    }

    /// <summary>The screen is going away. Called on every area, as Resume is.</summary>
    public virtual void Pause()
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
    /// Raised when the area's content is stale. The shell redraws only if this area is
    /// the one showing — a background sync finishing behind another tab should not
    /// rebuild that tab.
    /// </summary>
    public event Action? Changed;

    protected void Invalidate() => Changed?.Invoke();
}
