using Android.Content;
using Android.Graphics;
using Android.Views;
using Constitution.Domain.Scrapbook;
using Constitution.Domain.Scraps;

namespace Constitution.Scrapbook;

/// <summary>
/// A page of scraps (D41, D47): the glue between the views and the pure <see cref="ScrapLayout"/>.
/// It measures each scrap's content at the width the layout gives every scrap, hands those heights
/// to <c>ScrapLayout.Place</c>, and puts each <see cref="ScrapView"/> exactly where it says,
/// turned by its tilt, with a strip of tape drawn over each from the layout's own tape anchors.
/// Nothing here decides where anything goes. It is as wide as the well, edge to edge, because
/// the layout's x already counts the well's padding; give it to a scroll view to scroll it.
/// </summary>
public sealed class ScrapHost : ViewGroup, IFullBleedPage
{
    /// <summary>Narrowest well the layout can place a scrap in: its two paddings, both side margins, and a dp to spare.</summary>
    private const double NarrowestWellDp =
        ScrapMetrics.WellPaddingStartDp + ScrapMetrics.WellPaddingEndDp + (2 * ScrapMetrics.SideMarginDp) + 1;

    private readonly TapePainter tape = new();
    private readonly List<ScrapView> views = [];
    private IReadOnlyList<ScrapEntry> entries = [];
    private IReadOnlyList<PlacedScrap> placed = [];

    public ScrapHost(Context context) : base(context)
    {
        // Tilted corners and the tape's overhang are allowed to leave the box a little.
        SetClipChildren(false);
    }

    private float Density => Context!.Resources!.DisplayMetrics!.Density;

    /// <summary>Replaces the page's scraps, top to bottom.</summary>
    public void SetScraps(IReadOnlyList<ScrapEntry> scraps)
    {
        ArgumentNullException.ThrowIfNull(scraps);

        RemoveAllViews();
        views.Clear();
        entries = scraps;
        placed = [];
        foreach (var entry in scraps)
        {
            var view = new ScrapView(Context!, entry, ScrapPainters.For(entry.Kind));
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
        if (entries.Count == 0 || wellWidthDp < NarrowestWellDp)
        {
            placed = [];
            SetMeasuredDimension(widthPx, ResolveSize(0, heightMeasureSpec));
            return;
        }

        // Every scrap is the same width, so one probe pass tells each content the width to measure at.
        var probe = ScrapLayout.Place(entries.Select(e => new Scrap(e.Kind, e.Id, 0)).ToList(), wellWidthDp);
        var scraps = new List<Scrap>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var insets = ScrapBox.Content(entry.Kind);
            var contentWidthPx = (int)Math.Round((probe[i].Width - insets.Left - insets.Right) * density);
            entry.Content.Measure(
                MeasureSpec.MakeMeasureSpec(Math.Max(contentWidthPx, 0), MeasureSpecMode.Exactly),
                MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));
            scraps.Add(new Scrap(entry.Kind, entry.Id, entry.Content.MeasuredHeight / density));
        }

        placed = ScrapLayout.Place(scraps, wellWidthDp);
        for (var i = 0; i < placed.Count; i++)
        {
            views[i].Measure(
                MeasureSpec.MakeMeasureSpec((int)Math.Round(placed[i].Width * density), MeasureSpecMode.Exactly),
                MeasureSpec.MakeMeasureSpec((int)Math.Round(placed[i].Height * density), MeasureSpecMode.Exactly));
        }

        var heightPx = (int)Math.Ceiling(ScrapLayout.StackHeightDp(placed) * density);
        SetMeasuredDimension(widthPx, ResolveSize(heightPx, heightMeasureSpec));
    }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        var density = Density;
        for (var i = 0; i < placed.Count; i++)
        {
            var scrap = placed[i];
            var x = (int)Math.Round(scrap.X * density);
            var y = (int)Math.Round(scrap.Y * density);
            views[i].Layout(x, y, x + views[i].MeasuredWidth, y + views[i].MeasuredHeight);
            views[i].Rotation = (float)scrap.TiltDegrees;
        }
    }

    protected override void DispatchDraw(Canvas canvas)
    {
        base.DispatchDraw(canvas);

        // The tape lies over the scrap, so it is drawn after the children, in the well's own space.
        var density = Density;
        canvas.Save();
        canvas.Scale(density, density);
        foreach (var scrap in placed)
        {
            tape.Draw(canvas, scrap.TapeAnchor);
        }

        canvas.Restore();
    }
}
