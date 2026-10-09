using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Ui;
using Pulp.Domain.Shell;

namespace Pulp.Shell;

/// <summary>
/// One of Pulp's top-level areas — what sits behind one binder tab. Post's and Score's split: an
/// area owns its own state and builds its own pane, the shell owns the frame and which area is
/// showing. An area asks to be redrawn by raising <see cref="Changed"/> rather than reaching into
/// the shell, and answers Back for itself, because only it knows whether it is drilled into
/// something.
///
/// <para>Who an area is — its label, header, glyph and hue — comes from <see cref="PulpAreas"/>,
/// so the shell never carries a second table of it.</para>
/// </summary>
public abstract class PulpAreaPage(Activity host, PulpArea area)
{
    protected Activity Host { get; } = host;

    public PulpAreaSpec Spec { get; } = PulpAreas.For(area);

    /// <summary>
    /// The frame the shell draws the page in, and the one a dog-ear or an action card overlays.
    /// Set once the shell has built its layout, since the areas exist before the frame does.
    /// </summary>
    public FrameLayout Overlay { get; set; } = null!;

    /// <summary>How this area appears in the binder gutter (DESIGN.md §9).</summary>
    public BinderArea Tab
    {
        get
        {
            var (ink, cloth, _) = AreaPalette.For(Spec.Hue);
            return new BinderArea(Spec.Label, AreaPalette.Glyph(Spec.Area), ink, cloth);
        }
    }

    /// <summary>The in-page Serif header (§7).</summary>
    public virtual string Title => Spec.Title;

    /// <summary>The colour of the header's marker underline: the identity, never the tab's role (§9).</summary>
    public Color Rule => AreaPalette.For(PulpAreas.RuleHue(Spec.Area)).Rule;

    /// <summary>
    /// The owner's voice under the header, or null to keep the seat hidden. Hidden until composed
    /// (D118), so the default is null.
    /// </summary>
    public virtual string? Greeting => null;

    /// <summary>
    /// The area's content, built fresh on every render. Until a screen lands, the page says what will
    /// gather there in one quiet sentence — never a spinner and never invented rows.
    /// </summary>
    public virtual View BuildView()
    {
        var status = PaperUi.Secondary(Host, Spec.Status);
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        column.AddView(status, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return column;
    }

    /// <summary>The area's pinned bottom bar, or null when it has none.</summary>
    public virtual View? BuildBottomBar() => null;

    /// <summary>Back, offered to the area first. True when it was used to come up a level.</summary>
    public virtual bool GoBack() => false;

    /// <summary>The screen came back. Called on every area, showing or not.</summary>
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

    /// <summary>Raised when the area's content is stale; the shell redraws only if it is showing.</summary>
    public event Action? Changed;

    protected void Invalidate() => Changed?.Invoke();
}
