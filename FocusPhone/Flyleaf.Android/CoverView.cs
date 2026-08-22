using Android.Content;
using Android.Graphics;
using Android.Views;
using Codex.Ui;

namespace Flyleaf;

/// <summary>
/// The Flyleaf book cover, drawn as structural vectors: the warm identity ground, an ex-libris
/// bookplate (fine double-rule + corner flourishes), the "arch-into-study" illustration window,
/// the lower title band ("FLYLEAF" / "STORYWORTHTELLING"), and an embossed maker's mark. The
/// illustration window holds the Gemini-painted arch-into-study cover art (committed drawable
/// cover_flyleaf), center-cropped to fill; it falls back to a firelight wash only if the bitmap
/// can't be decoded. Shown full-bleed on the cover-load overlay; the anatomy matches the approved mockup.
/// </summary>
public class CoverView : View
{
    private readonly float density;

    private static readonly Paint ImagePaint = new(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
    private Bitmap? coverArt;
    private bool coverLoaded;

    public CoverView(Context context) : base(context)
    {
        density = context.Resources!.DisplayMetrics!.Density;
    }

    // The Gemini-painted cover, decoded once and reused; null if it can't be decoded (low memory), in
    // which case the window falls back to the firelight wash.
    private Bitmap? CoverArt
    {
        get
        {
            if (!coverLoaded)
            {
                coverLoaded = true;
                try { coverArt = BitmapFactory.DecodeResource(Resources, Resource.Drawable.cover_flyleaf); }
                catch (Exception) { coverArt = null; }
            }
            return coverArt;
        }
    }

    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        coverArt?.Recycle();
        coverArt = null;
        coverLoaded = false;
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        float w = Width, h = Height;

        // Warm identity ground.
        using (var ground = new Paint { AntiAlias = true })
        {
            ground.SetShader(new LinearGradient(0, 0, 0, h,
                PaperTheme.IdentityWarm, PaperTheme.IdentityDeep, Shader.TileMode.Clamp!));
            canvas.DrawRect(0, 0, w, h, ground);
        }

        // The bookplate: leave ground at the bottom for the voiced hello.
        var side = 26 * density;
        var plate = new RectF(side, h * 0.09f, w - side, h * 0.80f);
        using (var card = new Paint { AntiAlias = true, Color = PaperTheme.Paper })
        {
            canvas.DrawRoundRect(plate, 6 * density, 6 * density, card);
        }
        DrawDoubleRule(canvas, plate);
        DrawCornerFlourishes(canvas, Inset(plate, 8 * density));

        // Illustration window (arch-into-study) — wash placeholder for the Gemini paint.
        var window = new RectF(plate.Left + 18 * density, plate.Top + 18 * density,
            plate.Right - 18 * density, plate.Top + plate.Height() * 0.60f);
        DrawIllustrationPlaceholder(canvas, window);

        // Title band.
        DrawTitle(canvas, plate, window.Bottom);

        // Embossed maker's mark near the plate foot.
        DrawMakersMark(canvas, plate.CenterX(), plate.Bottom - 26 * density);
    }

    private void DrawDoubleRule(Canvas canvas, RectF plate)
    {
        using var rule = new Paint { AntiAlias = true, Color = PaperTheme.Ink, StrokeWidth = 1.5f * density };
        rule.SetStyle(Paint.Style.Stroke);
        var outer = Inset(plate, 8 * density);
        var inner = Inset(plate, 12 * density);
        canvas.DrawRoundRect(outer, 4 * density, 4 * density, rule);
        rule.Color = PaperTheme.InkSoft;
        rule.StrokeWidth = 1 * density;
        canvas.DrawRoundRect(inner, 3 * density, 3 * density, rule);
    }

    private void DrawCornerFlourishes(Canvas canvas, RectF r)
    {
        using var flourish = new Paint { AntiAlias = true, Color = PaperTheme.IdentityMark, StrokeWidth = 1.4f * density };
        flourish.SetStyle(Paint.Style.Stroke);
        var d = 9 * density;
        // Short right-angle ticks just inside each corner.
        DrawCorner(canvas, r.Left, r.Top, d, d, flourish);
        DrawCorner(canvas, r.Right, r.Top, -d, d, flourish);
        DrawCorner(canvas, r.Left, r.Bottom, d, -d, flourish);
        DrawCorner(canvas, r.Right, r.Bottom, -d, -d, flourish);
    }

    private static void DrawCorner(Canvas canvas, float x, float y, float dx, float dy, Paint paint)
    {
        canvas.DrawLine(x, y, x + dx, y, paint);
        canvas.DrawLine(x, y, x, y + dy, paint);
    }

    private void DrawIllustrationPlaceholder(Canvas canvas, RectF window)
    {
        var art = CoverArt;
        if (art is not null)
        {
            // The Gemini-painted arch-into-study cover, center-cropped to fill the window.
            canvas.Save();
            canvas.ClipRect(window);
            var src = SourceCropFor(art, window);
            canvas.DrawBitmap(art, src, window, ImagePaint);
            canvas.Restore();
        }
        else
        {
            // Fallback wash if the drawable can't be decoded (e.g. low memory).
            using var wash = new Paint { AntiAlias = true };
            wash.SetShader(new RadialGradient(window.CenterX(), window.Bottom, window.Height(),
                PaperTheme.Firelight, PaperTheme.WalnutDeep, Shader.TileMode.Clamp!));
            canvas.DrawRect(window, wash);
        }
        // Keyline around the window.
        using var edge = new Paint { AntiAlias = true, Color = PaperTheme.Ink, StrokeWidth = 1 * density };
        edge.SetStyle(Paint.Style.Stroke);
        canvas.DrawRect(window, edge);
    }

    // Center-crop: pick the source sub-rect of the painting whose aspect matches the window, so the art
    // fills the window without distortion or letterboxing.
    private static Rect SourceCropFor(Bitmap art, RectF window)
    {
        float bw = art.Width, bh = art.Height;
        var windowAspect = window.Width() / window.Height();
        var cropW = bw;
        var cropH = bw / windowAspect;
        if (cropH > bh)
        {
            cropH = bh;
            cropW = bh * windowAspect;
        }
        var left = (bw - cropW) / 2f;
        var top = (bh - cropH) / 2f;
        return new Rect((int)left, (int)top, (int)(left + cropW), (int)(top + cropH));
    }

    private void DrawTitle(Canvas canvas, RectF plate, float top)
    {
        using var title = new Paint(PaintFlags.AntiAlias) { Color = PaperTheme.Ink, TextAlign = Paint.Align.Center };
        title.SetTypeface(PaperTheme.SerifSemibold(Context!));
        title.TextSize = Math.Min(plate.Width() * 0.16f, 44 * density);
        var titleY = top + (plate.Bottom - top) * 0.42f;
        canvas.DrawText("FLYLEAF", plate.CenterX(), titleY, title);

        using var sub = new Paint(PaintFlags.AntiAlias) { Color = PaperTheme.InkMuted, TextAlign = Paint.Align.Center };
        sub.SetTypeface(PaperTheme.SansRegular(Context!));
        sub.TextSize = Math.Min(plate.Width() * 0.035f, 12 * density);
        sub.LetterSpacing = 0.25f;
        canvas.DrawText("STORYWORTHTELLING", plate.CenterX(), titleY + 22 * density, sub);
    }

    private void DrawMakersMark(Canvas canvas, float cx, float cy)
    {
        var radius = 12 * density;
        using var ring = new Paint { AntiAlias = true, Color = PaperTheme.IdentityMark, StrokeWidth = 1.4f * density };
        ring.SetStyle(Paint.Style.Stroke);
        canvas.DrawCircle(cx, cy, radius, ring);
        using var m = new Paint(PaintFlags.AntiAlias) { Color = PaperTheme.IdentityMark, TextAlign = Paint.Align.Center };
        m.SetTypeface(PaperTheme.SerifSemibold(Context!));
        m.TextSize = radius * 1.1f;
        canvas.DrawText("M", cx, cy + m.TextSize / 3f, m);
    }

    private static RectF Inset(RectF r, float d) => new(r.Left + d, r.Top + d, r.Right - d, r.Bottom - d);
}
