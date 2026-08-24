using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;

// System.IO.Path arrives via ImplicitUsings; drawing code means the graphics one.
using Path = Android.Graphics.Path;

namespace Codex.Ui;

/// <summary>
/// The frame around someone else's picture (DESIGN.md §7, Artwork slot).
///
/// Album and podcast art is <b>content, not palette</b> — it keeps whatever colour
/// it arrives with. What the system governs is the <b>window</b>: a hairline keyline
/// and a small radius, and the art <b>never bled to the edge</b>. That single device
/// is what lets a saturated cover sit on cream paper without either one losing.
///
/// Missing art falls back to the miniature bookplate, so a ragged library reads as a
/// shelf of plain bindings rather than as a grid of broken images.
/// </summary>
public class ArtworkWindow : FrameLayout
{
    /// <summary>Beside a row, where the art distinguishes one row from the next.</summary>
    public const int RowSizeDp = 40;

    /// <summary>The header of a work's own page, where the art is worth looking at.</summary>
    public const int DetailSizeDp = 104;

    // A window is a window, not a card — the corner is barely there. §5's soft
    // stationery radii govern containers; this is a keyline around a picture.
    private const int KeylineRadiusDp = 3;

    private readonly ImageView art;
    private readonly BookplateView fallback;

    public ArtworkWindow(Context context) : base(context)
    {
        var shape = new GradientDrawable();
        shape.SetColor(PaperTheme.PaperDeep);
        shape.SetCornerRadius(PaperTheme.Dp(context, KeylineRadiusDp));
        shape.SetStroke(PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp), PaperTheme.Hairline);
        Background = shape;
        ClipToOutline = true;

        fallback = new BookplateView(context);
        AddView(fallback, new LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        art = new ImageView(context) { Visibility = ViewStates.Gone };
        art.SetScaleType(ImageView.ScaleType.CenterCrop);
        AddView(art, new LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
    }

    /// <summary>Show a cover, or fall back to the bookplate when there is none.</summary>
    public void SetArtwork(Bitmap? bitmap)
    {
        if (bitmap is null)
        {
            art.Visibility = ViewStates.Gone;
            fallback.Visibility = ViewStates.Visible;
            return;
        }

        art.SetImageBitmap(bitmap);
        art.Visibility = ViewStates.Visible;
        fallback.Visibility = ViewStates.Gone;
    }

    public void SetArtwork(Drawable? drawable)
    {
        if (drawable is null)
        {
            art.Visibility = ViewStates.Gone;
            fallback.Visibility = ViewStates.Visible;
            return;
        }

        art.SetImageDrawable(drawable);
        art.Visibility = ViewStates.Visible;
        fallback.Visibility = ViewStates.Gone;
    }

    /// <summary>
    /// The miniature ex-libris used when a work publishes no art: an inner rule and
    /// an arched window, drawn rather than shipped so it is crisp at any size.
    /// </summary>
    private class BookplateView(Context context) : View(context)
    {
        private readonly Paint line = new()
        {
            AntiAlias = true,
            Color = PaperTheme.InkMuted,
            StrokeWidth = PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp),
            Alpha = 150,
        };

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            line.SetStyle(Paint.Style.Stroke);

            var w = Width;
            var h = Height;
            var inset = Math.Min(w, h) * 0.14f;

            // The inner rule, inset from the keyline.
            canvas.DrawRect(inset, inset, w - inset, h - inset, line);

            // The arch: a shallow dome on two uprights, standing on a base line.
            var archLeft = w * 0.30f;
            var archRight = w * 0.70f;
            var archTop = h * 0.34f;
            var baseline = h * 0.70f;

            var path = new Path();
            path.MoveTo(archLeft, baseline);
            path.LineTo(archLeft, archTop + ((baseline - archTop) * 0.35f));
            path.QuadTo(w * 0.5f, archTop - ((baseline - archTop) * 0.20f),
                archRight, archTop + ((baseline - archTop) * 0.35f));
            path.LineTo(archRight, baseline);
            canvas.DrawPath(path, line);
            canvas.DrawLine(archLeft - (w * 0.04f), baseline, archRight + (w * 0.04f), baseline, line);
        }
    }
}
