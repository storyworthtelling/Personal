using Android.Content;
using Android.Graphics;
using Android.Views;
using Constitution.Domain.Workouts.Screens;
using Constitution.Scrapbook;

namespace Constitution.WorkoutsTab;

/// <summary>
/// One clipping on the Workouts tab: the paper decided by <see cref="ClippingPaper"/>, the content a child sitting inside the
/// padding <see cref="ClippingLayout"/> assumed, and a long-press that raises the day's card. A tap does nothing: the rows
/// open nothing (book p.12).
/// </summary>
internal sealed class ClippingView : ViewGroup
{
    private readonly ClippingPaper paper;
    private readonly View content;
    private readonly string? folio;
    private readonly bool isLarge;
    private readonly bool isPressed;

    public ClippingView(Context context, bool isLarge, View content, string? folio, bool pressed, Action? onHold) : base(context)
    {
        this.isLarge = isLarge;
        this.content = content;
        this.folio = folio;
        isPressed = pressed;
        paper = new ClippingPaper(isLarge);
        SetWillNotDraw(false);
        SetClipChildren(false);
        (content.Parent as ViewGroup)?.RemoveView(content);
        AddView(content);
        if (onHold is not null)
        {
            LongClick += (_, _) => onHold();
        }
    }

    private float Density => Context!.Resources!.DisplayMetrics!.Density;

    private int Start => Px(isLarge ? ClippingLayout.LargePaddingStartDp : ClippingLayout.SmallPaddingSideDp);

    private int End => Px(isLarge ? ClippingLayout.LargePaddingEndDp : ClippingLayout.SmallPaddingSideDp);

    private int Top => Px(isLarge ? ClippingLayout.LargePaddingTopDp : ClippingLayout.SmallPaddingTopDp);

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        var height = MeasureSpec.GetSize(heightMeasureSpec);
        content.Measure(
            MeasureSpec.MakeMeasureSpec(Math.Max(width - Start - End, 0), MeasureSpecMode.Exactly),
            MeasureSpec.MakeMeasureSpec(Math.Max(height, 0), MeasureSpecMode.AtMost));
        SetMeasuredDimension(width, height);
    }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom) =>
        content.Layout(Start, Top, Start + content.MeasuredWidth, Top + content.MeasuredHeight);

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var density = Density;
        canvas.Save();
        canvas.Scale(density, density);
        paper.Draw(canvas, new ScrapSurface(Context!, Width / density, Height / density, folio), isPressed);
        canvas.Restore();
    }

    private int Px(double dp) => (int)Math.Round(dp * Density);
}
