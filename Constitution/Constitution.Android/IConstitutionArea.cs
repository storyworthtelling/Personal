using Android.Views;
using Android.Widget;
using Codex.Ui;
using Constitution.Domain.Areas;
using Constitution.Domain.Ground;

namespace Constitution;

/// <summary>
/// One of Constitution's five areas: what sits behind one binder tab. The split is Post's and
/// Score's: an area owns what it shows, the shell owns the frame and which area is turned to.
/// Who the areas are, their order, cloths and ids come from <see cref="ConstitutionAreas"/>; this
/// carries only what is drawn.
/// </summary>
public interface IConstitutionArea
{
    /// <summary>The domain's definition of this tab: id, name, cloth and lettering tokens.</summary>
    ConstitutionArea Definition { get; }

    /// <summary>How the tab is drawn in the binder gutter.</summary>
    BinderArea Tab { get; }

    /// <summary>The in-page Serif Black header text.</summary>
    string Title { get; }

    /// <summary>Plain for Settings, crinkled for the rest (D43, D71).</summary>
    PageGround Ground { get; }

    /// <summary>
    /// The seam for the ground: the shell passes the well through this, so the crinkle and the
    /// scraps (a later wave) arrive without the shell changing.
    /// </summary>
    IPageGroundPainter GroundPainter { get; }

    /// <summary>
    /// The frame a dog-ear or an action card overlays, handed over once the shell has built its
    /// layout (the areas exist before the frame does).
    /// </summary>
    FrameLayout Overlay { get; set; }

    /// <summary>The well's content, built fresh on every render.</summary>
    View BuildView();

    /// <summary>The area's actions for the pinned bottom bar; null when it has none.</summary>
    View? BuildBottomBar();

    /// <summary>Raised when the content is stale; the shell redraws only if this area is showing.</summary>
    event Action? Changed;

    void Resume();

    void Pause();
}
