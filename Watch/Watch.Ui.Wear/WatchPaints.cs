using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Util;
using Watch.Domain.Design;

namespace Watch.Ui.Wear;

/// <summary>
/// The kit's one way to turn a token into paint (WD §2, §13): every colour comes from
/// <see cref="WatchTokens"/> or <see cref="DayColour"/> through <see cref="Srgb"/>, and glass glows in
/// its own colour. A painter holds no colour of its own. Public, so an app's own views paint the same way.
/// </summary>
public static class WatchPaints
{
    /// <summary>The colour of a token, with <paramref name="alpha"/> from 0 to 1.</summary>
    public static Color Of(Oklch colour, double alpha = 1) => new(Srgb.Argb(colour, alpha));

    /// <summary>An app's day colour now, lifted for its glyph where the tokens say (Dash).</summary>
    public static Oklch Glass(WatchApp app, TimeOnly now)
    {
        var colour = DayColour.At(app, now);
        return colour with { L = Math.Clamp(colour.L + WatchTokens.GlyphLift(app), 0, 1) };
    }

    /// <summary>A round-capped stroke of glass, glowing in its own colour when <paramref name="glow"/> is above zero.</summary>
    public static Paint Stroke(Oklch colour, float width, double alpha = 1, float glow = 0)
    {
        var paint = new Paint(PaintFlags.AntiAlias)
        {
            StrokeWidth = width,
            StrokeCap = Paint.Cap.Round!,
            StrokeJoin = Paint.Join.Round!,
            Color = Of(colour, alpha),
        };
        paint.SetStyle(Paint.Style.Stroke);
        if (glow > 0)
        {
            paint.SetShadowLayer(glow, 0, 0, Of(colour, alpha));
        }
        return paint;
    }

    /// <summary>A path through <paramref name="points"/>, or none for fewer than two.</summary>
    public static Android.Graphics.Path? Line(IReadOnlyList<Watch.Domain.Face.ShapePoint> points)
    {
        if (points.Count < 2)
        {
            return null;
        }
        var path = new Android.Graphics.Path();
        path.MoveTo((float)points[0].X, (float)points[0].Y);
        for (var index = 1; index < points.Count; index++)
        {
            path.LineTo((float)points[index].X, (float)points[index].Y);
        }
        return path;
    }

    /// <summary>The time of day the day colours are read at.</summary>
    public static TimeOnly Now() => TimeOnly.FromDateTime(DateTime.Now);

    /// <summary>A touch target's side in pixels (WD §12: at least 48 dp).</summary>
    public static float TouchTarget(Context context) => WatchTokens.TouchTargetDp * (context.Resources?.DisplayMetrics?.Density ?? 1);

    /// <summary>One of the three type sizes (WD §5), in pixels on this screen.</summary>
    public static float SizeOf(Context context, WatchType type) =>
        TypedValue.ApplyDimension(ComplexUnitType.Sp, WatchTextView.SizeOf(type), context.Resources?.DisplayMetrics);

    /// <summary>Words drawn on a canvas: Plex Mono at one of the three sizes, in one of the tokens.</summary>
    public static TextPaint Words(Context context, WatchType type, WatchWeight weight, Oklch colour, double alpha = 1)
    {
        var paint = new TextPaint(PaintFlags.AntiAlias)
        {
            Color = Of(colour, alpha),
        };
        paint.SetTypeface(WatchFonts.Of(context, weight));
        paint.TextSize = SizeOf(context, type);
        return paint;
    }

    /// <summary>A filled shape with round corners, for a card or a message (WD §2: the system's own cards are the one grey ground).</summary>
    public static GradientDrawable Rounded(Oklch colour, float corner, double alpha = 1)
    {
        var shape = new GradientDrawable();
        shape.SetShape(ShapeType.Rectangle);
        shape.SetCornerRadius(corner);
        shape.SetColor(Of(colour, alpha));
        return shape;
    }
}
