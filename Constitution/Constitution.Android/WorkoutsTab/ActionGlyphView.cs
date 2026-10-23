using Android.Content;
using Android.Graphics;
using Android.Views;
using Codex.Ui;
using Path = Android.Graphics.Path;

namespace Constitution.WorkoutsTab;

/// <summary>The two small line glyphs on the action card (book p.12): a cross for Skip, two opposed arrows for Swap. Drawn on the mockup's 18 unit grid in ink.</summary>
internal sealed class ActionGlyphView : View
{
    private const int GridUnits = 18;
    private const float StrokeUnits = 1.6f;
    private const int SizeDp = 18;

    private readonly Path path = new();
    private readonly Paint stroke = new() { AntiAlias = true };

    public ActionGlyphView(Context context, bool isSkip) : base(context)
    {
        stroke.SetStyle(Paint.Style.Stroke);
        stroke.Color = PaperTheme.Ink;
        stroke.StrokeCap = Paint.Cap.Round;
        stroke.StrokeJoin = Paint.Join.Round;
        if (isSkip)
        {
            path.MoveTo(4, 4);
            path.LineTo(14, 14);
            path.MoveTo(14, 4);
            path.LineTo(4, 14);
        }
        else
        {
            path.MoveTo(3, 6);
            path.LineTo(14, 6);
            path.LineTo(11, 3);
            path.MoveTo(15, 12);
            path.LineTo(4, 12);
            path.LineTo(7, 15);
        }
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var size = PaperTheme.Dp(Context!, SizeDp);
        SetMeasuredDimension(size, size);
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var scale = Width / (float)GridUnits;
        stroke.StrokeWidth = StrokeUnits * scale;
        canvas.DrawPath(Scaled(scale), stroke);
    }

    private Path Scaled(float scale)
    {
        var matrix = new Matrix();
        matrix.SetScale(scale, scale);
        var result = new Path();
        path.Transform(matrix, result);
        return result;
    }
}
