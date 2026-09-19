using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;

namespace Codex.Ui;

/// <summary>
/// One area the binder can turn to. It carries TWO colours because a tab is coloured
/// differently depending on which side of the seam it is on (§9): <paramref name="Cloth"/>
/// is the area's fill tier, the coloured cloth an INACTIVE tab is cut from, and
/// <paramref name="Hue"/> is the mark tier the ACTIVE tab sets its glyph and label in
/// once the tab itself has gone page-coloured.
/// </summary>
public sealed record BinderArea(string Label, int GlyphResourceId, Color Hue, Color Cloth);

/// <summary>
/// In-app navigation as the right-edge divider tabs of a school notebook
/// (DESIGN.md §9). A <b>page-edge line</b> runs down the left of the gutter;
/// inactive tabs sit outside it in their own area's cloth, pointing out toward the
/// screen edge; the <b>active tab is page-coloured and open on the page side</b>, so
/// it blends into the content at the seam instead of crossing into it.
///
/// Positions come from the pure <see cref="BinderTabLayout"/>, so the rule that
/// every tab clears §5's 48dp minimum — and that the strip scrolls rather than
/// shrinking past that — is decided and tested away from the device. This is a
/// ViewGroup rather than a painter so each tab is a real view: free text layout,
/// free accessibility, free touch feedback.
///
/// Tapping the tab you are already on is how you return that area to its root, so
/// the callback fires for the active index too rather than being swallowed.
/// </summary>
public class BinderTabs : ViewGroup
{
    /// <summary>The reserved gutter. Wide enough for the longest area label.</summary>
    public const int WidthDp = 66;

    private readonly List<BinderArea> areas = [];
    private readonly Paint pageEdge;
    private readonly Paint gutter;
    private readonly Paint gutterGrain;
    private readonly int radius;
    private int activeIndex;

    public BinderTabs(Context context) : base(context)
    {
        SetWillNotDraw(false);
        radius = DesignTokens.Shape.RadiusCard;
        pageEdge = new Paint
        {
            AntiAlias = true,
            Color = PaperTheme.Hairline,
            StrokeWidth = PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp),
        };
        pageEdge.SetStyle(Paint.Style.Stroke);

        // Fills the whole gutter behind the tabs — see OnDraw's remarks for why this
        // exists at all.
        gutter = new Paint { AntiAlias = true, Color = PaperTheme.Paper };
        gutter.SetStyle(Paint.Style.Fill);

        // The gaps read as flat vector color without this — the same grain the tabs'
        // own cloth carries (DESIGN.md §2), drawn as a second pass over the same rect.
        gutterGrain = PaperTheme.GrainOverlay(context, gutter);
    }

    /// <summary>Fires with the tapped index — including the active one.</summary>
    public event Action<int>? AreaSelected;

    public void SetAreas(IEnumerable<BinderArea> value, int active)
    {
        areas.Clear();
        areas.AddRange(value);
        activeIndex = active;
        UpdateGutterColor();

        RemoveAllViews();
        for (var i = 0; i < areas.Count; i++)
        {
            AddView(BuildTab(areas[i], i));
        }

        Invalidate();
    }

    public void SetActive(int index)
    {
        activeIndex = index;
        UpdateGutterColor();
        for (var i = 0; i < ChildCount; i++)
        {
            PaintTab(GetChildAt(i)!, areas[i], i == index);
        }

        Invalidate();
    }

    // The active area's own cloth, matching the fill an inactive tab of that area
    // would wear (PaintTab) — the nearest thing this class has to "the cover color."
    private void UpdateGutterColor()
    {
        if (activeIndex >= 0 && activeIndex < areas.Count)
        {
            gutter.Color = areas[activeIndex].Cloth;
        }
    }

    private View BuildTab(BinderArea area, int index)
    {
        var tab = new LinearLayout(Context!) { Orientation = Orientation.Vertical };
        tab.SetGravity(GravityFlags.Center);

        var glyph = new ImageView(Context!);
        glyph.SetImageResource(area.GlyphResourceId);
        var size = PaperTheme.Dp(Context!, 26);
        tab.AddView(glyph, new LinearLayout.LayoutParams(size, size));

        var label = PaperUi.Label(Context!, area.Label);
        label.SetSingleLine(true);
        label.Gravity = GravityFlags.Center;
        tab.AddView(label, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(Context!, DesignTokens.Space.X1),
        });

        tab.Click += (_, _) => AreaSelected?.Invoke(index);
        PaintTab(tab, area, index == activeIndex);
        return tab;
    }

    private void PaintTab(View tab, BinderArea area, bool active)
    {
        // The active tab is page-coloured and rounded only on the outer side, so it
        // reads as the page continuing under the seam rather than as a chip. An
        // inactive tab is cut from its own area's cloth, so the strip reads as coded
        // dividers at rest — the tab carries the colour, not the glyph on it.
        var fill = active ? PaperTheme.Paper : area.Cloth;
        var shape = new GradientDrawable();
        shape.SetColor(fill);
        shape.SetStroke(PaperTheme.Dp(Context!, DesignTokens.Shape.HairlineDp), PaperTheme.Hairline);

        float r = PaperTheme.Dp(Context!, radius);
        // Corners run top-left, top-right, bottom-right, bottom-left as x/y pairs.
        shape.SetCornerRadii([0f, 0f, r, r, r, r, 0f, 0f]);
        tab.Background = shape;

        if (tab is LinearLayout column && column.ChildCount == 2)
        {
            var glyph = (ImageView)column.GetChildAt(0)!;
            var label = (TextView)column.GetChildAt(1)!;
            // On the page-coloured active tab the hue is the only thing marking the
            // area, so it goes in the ink. On the coloured cloth of an inactive tab
            // the cloth is already saying it, and tinting the glyph too would leave a
            // strip that reads as a row of disabled buttons.
            var tint = active ? area.Hue : PaperTheme.Ink;
            glyph.SetColorFilter(tint);
            label.SetTextColor(tint);
        }
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var width = PaperTheme.Dp(Context!, WidthDp);
        var height = MeasureSpec.GetSize(heightMeasureSpec);

        var strip = BinderTabLayout.Compute(ChildCount, PxToDp(height));
        for (var i = 0; i < ChildCount; i++)
        {
            GetChildAt(i)!.Measure(
                MeasureSpec.MakeMeasureSpec(width, MeasureSpecMode.Exactly),
                MeasureSpec.MakeMeasureSpec(PaperTheme.Dp(Context!, strip[i].HeightDp), MeasureSpecMode.Exactly));
        }

        SetMeasuredDimension(width, height);
    }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        var strip = BinderTabLayout.Compute(ChildCount, PxToDp(bottom - top));
        for (var i = 0; i < ChildCount; i++)
        {
            var tab = strip[i];
            var y = PaperTheme.Dp(Context!, tab.TopDp);

            // Inactive tabs sit a hairline outside the page edge and point away from
            // the page; the active one starts on the line so the seam closes.
            var inset = i == activeIndex ? 0 : PaperTheme.Dp(Context!, DesignTokens.Shape.HairlineDp);
            GetChildAt(i)!.Layout(inset, y, right - left, y + PaperTheme.Dp(Context!, tab.HeightDp));
        }
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);

        // BinderTabs never set a Background and its own OnDraw used to paint only the
        // page-edge hairline below — every tab child paints its own fill, but the lead-in
        // gap above the first tab and the small gaps BinderTabLayout leaves between tabs
        // belong to no view at all. Those pixels fell through to the Activity window's
        // own background (paper_background, the grain tile), which read as flecks of
        // paper texture inside what is supposed to be a solid binder gutter — worth
        // fixing on its own, and doubly so now that the grain is raised. Filling the
        // whole gutter first, before the hairline and before children draw over it, is
        // what makes the gaps read as the same cloth the tabs around them wear instead
        // of a hole back to the page underneath. The grain pass right after it is what
        // makes that cloth read as cloth rather than flat vector color — filling solved
        // the hole-back-to-the-page problem but left the fix itself looking synthetic
        // next to the textured paper it now sits beside (G3-04).
        canvas.DrawRect(0, 0, Width, Height, gutter);
        canvas.DrawRect(0, 0, Width, Height, gutterGrain);

        // The page edge: the line the tabs are clipped at, running the full height.
        var x = pageEdge.StrokeWidth / 2f;
        canvas.DrawLine(x, 0, x, Height, pageEdge);
    }

    private int PxToDp(int px) =>
        (int)(px / (Context!.Resources!.DisplayMetrics!.Density));
}
