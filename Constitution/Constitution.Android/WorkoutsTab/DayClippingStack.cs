using Android.Content;
using Android.Graphics;
using Android.Views;
using Constitution.Domain.Scraps;
using Constitution.Domain.Workouts.Screens;
using Constitution.Scrapbook;

namespace Constitution.WorkoutsTab;

/// <summary>One clipping for the stack: its content, whether it is today's large one, and what a long-press on it does (null for nothing).</summary>
internal sealed record ClippingItem(string Id, bool IsLarge, View Content, string? Folio, bool Pressed, Action? OnHold);

/// <summary>
/// The page of clippings: the glue between the views and the pure <see cref="ClippingLayout"/>. It measures each content at
/// the width the layout gives its kind, asks the layout where each goes, and puts each <see cref="ClippingView"/> there,
/// turned by its tilt, with a strip of tape drawn over each from the layout's own anchors. Nothing here decides where
/// anything goes. It is as wide as the well and given to a scroll view to scroll.
/// </summary>
internal sealed class DayClippingStack : ViewGroup, IFullBleedPage
{
    private const double NarrowestWellDp =
        ScrapMetrics.WellPaddingStartDp + ScrapMetrics.WellPaddingEndDp + (2 * ScrapMetrics.SideMarginDp) + 1;

    private readonly TapePainter tape = new();
    private readonly List<ClippingView> views = [];
    private IReadOnlyList<ClippingItem> items = [];
    private IReadOnlyList<PlacedClipping> placed = [];

    public DayClippingStack(Context context) : base(context)
    {
        SetClipChildren(false);
    }

    private float Density => Context!.Resources!.DisplayMetrics!.Density;

    public void SetClippings(IReadOnlyList<ClippingItem> clippings)
    {
        ArgumentNullException.ThrowIfNull(clippings);
        RemoveAllViews();
        views.Clear();
        items = clippings;
        placed = [];
        foreach (var item in clippings)
        {
            var view = new ClippingView(Context!, item.IsLarge, item.Content, item.Folio, item.Pressed, item.OnHold);
            views.Add(view);
            AddView(view);
        }

        RequestLayout();
        Invalidate();
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var widthPx = MeasureSpec.GetSize(widthMeasureSpec);
        var density = Density;
        var wellWidthDp = widthPx / density;
        if (items.Count == 0 || wellWidthDp < NarrowestWellDp)
        {
            placed = [];
            SetMeasuredDimension(widthPx, ResolveSize(0, heightMeasureSpec));
            return;
        }

        // Every clipping is the same width, so one probe pass tells each content the width to measure at.
        var probe = ClippingLayout.Place(items.Select(item => new ClippingSpec(item.Id, item.IsLarge, 0)).ToList(), wellWidthDp);
        var specs = new List<ClippingSpec>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var sides = item.IsLarge
                ? ClippingLayout.LargePaddingStartDp + ClippingLayout.LargePaddingEndDp
                : 2 * ClippingLayout.SmallPaddingSideDp;
            item.Content.Measure(
                MeasureSpec.MakeMeasureSpec((int)Math.Round(Math.Max(probe[index].Width - sides, 0) * density), MeasureSpecMode.Exactly),
                MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));
            specs.Add(new ClippingSpec(item.Id, item.IsLarge, item.Content.MeasuredHeight / density));
        }

        placed = ClippingLayout.Place(specs, wellWidthDp);
        for (var index = 0; index < placed.Count; index++)
        {
            views[index].Measure(
                MeasureSpec.MakeMeasureSpec((int)Math.Round(placed[index].Width * density), MeasureSpecMode.Exactly),
                MeasureSpec.MakeMeasureSpec((int)Math.Round(placed[index].Height * density), MeasureSpecMode.Exactly));
        }

        SetMeasuredDimension(widthPx, ResolveSize((int)Math.Ceiling(ClippingLayout.StackHeightDp(placed) * density), heightMeasureSpec));
    }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        var density = Density;
        for (var index = 0; index < placed.Count; index++)
        {
            var clipping = placed[index];
            var x = (int)Math.Round(clipping.X * density);
            var y = (int)Math.Round(clipping.Y * density);
            views[index].Layout(x, y, x + views[index].MeasuredWidth, y + views[index].MeasuredHeight);
            views[index].Rotation = (float)clipping.TiltDegrees;
        }
    }

    protected override void DispatchDraw(Canvas canvas)
    {
        base.DispatchDraw(canvas);

        // The tape lies over the clipping, so it is drawn after the children, in the well's own space.
        var density = Density;
        canvas.Save();
        canvas.Scale(density, density);
        foreach (var clipping in placed)
        {
            tape.Draw(canvas, clipping.Tape);
        }

        canvas.Restore();
    }
}
