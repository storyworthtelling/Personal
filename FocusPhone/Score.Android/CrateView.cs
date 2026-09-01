using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Apps;
using Codex.Domain.Design;
using Codex.Domain.Music;
using Codex.Ui;

namespace Score;

/// <summary>
/// This week's crate, drawn as a stack: the record you are looking at, and the run
/// behind it as stepped sleeve edges in the cloth of what is coming.
///
/// The steps are the whole argument for showing one record at a time. A list of
/// fourteen recommendations is a wall to get through; a stack you can see the depth
/// of is something you flick, and it is visibly shorter each time you do.
///
/// A vetoed record is <b>struck, not removed</b> — an editor's mark in ink-blue
/// across the sleeve (DESIGN.md §3's pen: a veto is an editorial act). It keeps its
/// place in the run so stepping back can lift it.
/// </summary>
public sealed class CrateView : FrameLayout
{
    /// <summary>The hero sleeve, at Now Playing's artwork size — this is the one
    /// thing on the screen worth looking at.</summary>
    public const int SleeveDp = 254;

    private readonly bool vetoed;
    private readonly int frontInset;
    private readonly Paint strike = new() { AntiAlias = true };

    public CrateView(Context context, CratePick pick, IReadOnlyList<CratePick> behind, bool vetoed)
        : base(context)
    {
        this.vetoed = vetoed;
        SetWillNotDraw(false);

        var step = PaperTheme.Dp(context, CrateGeometry.StepDp);
        var edges = CrateGeometry.VisibleEdges(behind.Count);
        frontInset = edges * step;

        // Furthest first, so each edge is overdrawn by the one in front of it and only
        // the sliver past its right and bottom shows. Edge i sits i steps down and
        // right of the front sleeve, which is what makes the stack read as depth
        // rather than as a border.
        for (var i = edges; i >= 1; i--)
        {
            AddView(Edge(context, behind[i - 1], i * step, (edges - i) * step));
        }

        var sleeve = new ArtworkWindow(context);
        sleeve.SetArtwork(CoverCache.Get(pick.ArtRef, PaperTheme.Dp(context, SleeveDp)));
        AddView(sleeve, new LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent)
        {
            RightMargin = frontInset,
            BottomMargin = frontInset,
        });

        strike.SetStyle(Paint.Style.Stroke);
        strike.StrokeWidth = PaperTheme.Dp(context, DesignTokens.Shape.WarningRuleDp);
        strike.StrokeCap = Paint.Cap.Round;
        strike.Color = PaperTheme.InkBlue;
    }

    // One sleeve edge: a slab of book cloth offset behind the front, showing only its
    // right and bottom margin. Its tone is bound to the record it belongs to, so the
    // stack's colours are the records that are coming rather than decoration.
    private static View Edge(Context context, CratePick pick, int leadingInset, int trailingInset)
    {
        var edge = new View(context);
        var shape = new Android.Graphics.Drawables.GradientDrawable();
        shape.SetColor(Color.ParseColor(BookClothPalette.For(pick.Id)));
        shape.SetCornerRadius(PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp) * 3f);
        edge.Background = shape;
        edge.LayoutParameters = new LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent)
        {
            LeftMargin = leadingInset,
            TopMargin = leadingInset,
            RightMargin = trailingInset,
            BottomMargin = trailingInset,
        };
        return edge;
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (!vetoed)
        {
            return;
        }

        // Corner to corner across the sleeve, not the whole stack: the mark belongs to
        // the record, and the run behind it is untouched.
        var inset = PaperTheme.Dp(Context!, DesignTokens.Space.X4);
        canvas.DrawLine(
            inset, Height - frontInset - inset,
            Width - frontInset - inset, inset, strike);
    }
}
