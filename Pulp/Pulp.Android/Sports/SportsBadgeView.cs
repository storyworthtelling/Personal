using Android.Content;
using Android.Graphics;
using Android.Views;
using Codex.Domain.Design;
using Codex.Ui;

namespace Pulp.Sports;

/// <summary>
/// A league's or team's badge (D238): content from the feed in an artwork slot. A league's is round, a
/// team's a shield, as mockup 07 draws them. Until the artwork arrives (or when there is none) it is a
/// monogram on the ink ground; the artwork, once set, is fitted inside the same outline.
/// </summary>
internal sealed class SportsBadgeView : View
{
    private readonly string monogram;
    private readonly bool shield;
    private readonly Paint fill = new(PaintFlags.AntiAlias);
    private readonly Paint stroke = new(PaintFlags.AntiAlias);
    private readonly Paint letters = new(PaintFlags.AntiAlias);
    private readonly Android.Graphics.Path outline = new();
    private Bitmap? art;

    public SportsBadgeView(Context context, string monogram, bool shield) : base(context)
    {
        this.monogram = monogram;
        this.shield = shield;
        fill.SetStyle(Paint.Style.Fill);
        fill.Color = PaperTheme.InkSoft;
        stroke.SetStyle(Paint.Style.Stroke);
        stroke.Color = PaperTheme.Ink;
        stroke.StrokeWidth = PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp);
        letters.Color = PaperTheme.PaperBright;
        letters.TextAlign = Paint.Align.Center;
        letters.SetTypeface(PaperTheme.SansSemibold(context));
    }

    public static int WidthDp => DesignTokens.Space.X6;

    public static int HeightDp(bool shield) => shield ? DesignTokens.Space.X6 + DesignTokens.Space.X1 : DesignTokens.Space.X6;

    public void SetArt(Bitmap? bitmap)
    {
        art = bitmap;
        Invalidate();
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec) =>
        SetMeasuredDimension(PaperTheme.Dp(Context!, WidthDp), PaperTheme.Dp(Context!, HeightDp(shield)));

    protected override void OnDraw(Canvas? canvas)
    {
        if (canvas is null)
        {
            return;
        }

        var inset = stroke.StrokeWidth;
        float w = Width - inset;
        float h = Height - inset;
        outline.Reset();
        if (shield)
        {
            outline.MoveTo(Width / 2f, inset);
            outline.LineTo(w, h * 0.16f);
            outline.LineTo(w, h * 0.5f);
            outline.QuadTo(w, h * 0.88f, Width / 2f, h);
            outline.QuadTo(inset, h * 0.88f, inset, h * 0.5f);
            outline.LineTo(inset, h * 0.16f);
            outline.Close();
        }
        else
        {
            outline.AddOval(new RectF(inset, inset, w, h), Android.Graphics.Path.Direction.Cw!);
        }

        canvas.Save();
        canvas.ClipPath(outline);
        if (art is { } picture)
        {
            fill.Color = PaperTheme.PaperBright;
            canvas.DrawPath(outline, fill);
            var scale = Math.Min(Width / (float)picture.Width, Height / (float)picture.Height);
            var drawn = new RectF(0, 0, picture.Width * scale, picture.Height * scale);
            drawn.Offset((Width - drawn.Width()) / 2f, (Height - drawn.Height()) / 2f);
            canvas.DrawBitmap(picture, null, drawn, null);
        }
        else
        {
            fill.Color = PaperTheme.InkSoft;
            canvas.DrawPath(outline, fill);
            letters.TextSize = Width * 0.34f;
            canvas.DrawText(monogram, Width / 2f, Height * 0.5f + letters.TextSize * 0.34f, letters);
        }

        canvas.Restore();
        canvas.DrawPath(outline, stroke);
    }
}

/// <summary>The fold's chevron: up when the line is open, down when it is one line.</summary>
internal sealed class SportsChevronView : View
{
    private readonly bool open;
    private readonly Paint paint = new(PaintFlags.AntiAlias);

    public SportsChevronView(Context context, bool open) : base(context)
    {
        this.open = open;
        paint.SetStyle(Paint.Style.Stroke);
        paint.Color = PaperTheme.InkMuted;
        paint.StrokeCap = Paint.Cap.Round;
        paint.StrokeWidth = PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp) * 1.6f;
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec) =>
        SetMeasuredDimension(PaperTheme.Dp(Context!, DesignTokens.Space.X4), PaperTheme.Dp(Context!, DesignTokens.Space.X4));

    protected override void OnDraw(Canvas? canvas)
    {
        if (canvas is null)
        {
            return;
        }

        float near = Height * (open ? 0.60f : 0.38f);
        float far = Height * (open ? 0.38f : 0.60f);
        canvas.DrawLine(Width * 0.25f, near, Width * 0.5f, far, paint);
        canvas.DrawLine(Width * 0.5f, far, Width * 0.75f, near, paint);
    }
}
