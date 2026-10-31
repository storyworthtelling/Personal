using Android.Content;
using Android.Graphics;
using Android.Views;
using Constitution.Domain.Scrapbook;

namespace Constitution.Scrapbook;

/// <summary>
/// One scrap: a box the size <c>ScrapLayout</c> decided, its paper drawn by the kind's thin painter
/// and its content a child sitting inside the border and padding the domain says. The tilt is the
/// view's own <see cref="View.Rotation"/>, which turns it about its centre as the mockup's CSS does.
/// </summary>
public sealed class ScrapView : ViewGroup
{
    private readonly IScrapPainter painter;
    private readonly View content;
    private readonly string? folio;
    private readonly BoxInsets insets;

    public ScrapView(Context context, ScrapEntry entry, IScrapPainter painter) : base(context)
    {
        this.painter = painter;
        content = entry.Content;
        folio = entry.Folio;
        insets = ScrapBox.Content(entry.Kind);
        SetWillNotDraw(false);
        SetClipChildren(false);

        // A content view can be handed to a fresh host after a page rebuild; take it from its old one.
        (content.Parent as ViewGroup)?.RemoveView(content);
        AddView(content);
    }

    private float Density => Context!.Resources!.DisplayMetrics!.Density;

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        var height = MeasureSpec.GetSize(heightMeasureSpec);
        var contentWidth = Math.Max(width - Px(insets.Left + insets.Right), 0);
        var contentHeight = Math.Max(height - Px(insets.Top + insets.Bottom), 0);

        content.Measure(
            MeasureSpec.MakeMeasureSpec(contentWidth, MeasureSpecMode.Exactly),
            MeasureSpec.MakeMeasureSpec(contentHeight, MeasureSpecMode.AtMost));
        SetMeasuredDimension(width, height);
    }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        var x = Px(insets.Left);
        var y = Px(insets.Top);
        content.Layout(x, y, x + content.MeasuredWidth, y + content.MeasuredHeight);
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var density = Density;
        canvas.Save();
        canvas.Scale(density, density);
        painter.DrawPaper(canvas, new ScrapSurface(Context!, Width / density, Height / density, folio));
        canvas.Restore();
    }

    private int Px(double dp) => (int)Math.Round(dp * Density);
}
