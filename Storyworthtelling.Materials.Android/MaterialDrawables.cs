using Android.Graphics;
using Android.Graphics.Drawables;
using Path = Android.Graphics.Path;

namespace Storyworthtelling.Materials.Android;

/// <summary>
/// The base of the material drawables: a drawable that knows when it is held (DESIGN.md §8), so a press lifts its own
/// texture instead of changing colour, and that ignores alpha and colour filters, which would flatten it.
/// </summary>
public abstract class MaterialDrawable(float density) : Drawable
{
    protected float Density { get; } = density;

    protected bool Pressed { get; private set; }

    public override bool IsStateful => true;

    public override int Opacity => (int)Format.Translucent;

    protected override bool OnStateChange(int[] state)
    {
        var pressed = state.Contains(global::Android.Resource.Attribute.StatePressed);
        if (pressed == Pressed)
        {
            return false;
        }
        Pressed = pressed;
        InvalidateSelf();
        return true;
    }

    public override void SetAlpha(int alpha)
    {
    }

    public override void SetColorFilter(ColorFilter? colorFilter)
    {
    }

    protected float Dp(float value) => value * Density;
}

/// <summary>
/// A marker fill (DESIGN.md §2): the primary button, a tag. Near-solid colour with fibre streaks running its length,
/// edges a little heavier where the felt pressed, the colour thinning in the last tenth. Held, the streaks deepen by
/// half and an inset hairline shows the press (§8); the colour never changes.
/// </summary>
public sealed class MarkerFillDrawable(Color fill, Color edgeInk, Color pressedRule, float radiusDp, float density) : MaterialDrawable(density)
{
    private readonly Paint body = new(PaintFlags.AntiAlias);
    private readonly Paint edge = new(PaintFlags.AntiAlias);
    private readonly Paint thin = new(PaintFlags.AntiAlias);
    private readonly Paint rule = new(PaintFlags.AntiAlias);

    public override void Draw(Canvas canvas)
    {
        var bounds = new RectF(Bounds);
        if (bounds.Width() <= 0 || bounds.Height() <= 0)
        {
            return;
        }
        var radius = Dp(radiusDp);

        var layer = canvas.SaveLayer(bounds, null);

        MaterialPainters.Marker(body, fill, Density, Pressed);
        body.SetStyle(Paint.Style.Fill);
        canvas.DrawRoundRect(bounds, radius, radius, body);

        // The heavier edge: the ink laid over the rim at the marker's edge strength.
        edge.SetStyle(Paint.Style.Stroke);
        edge.StrokeWidth = Dp(1);
        edge.Color = new Color((int)edgeInk.R, (int)edgeInk.G, (int)edgeInk.B, (int)Math.Round(MaterialStrength.MarkerEdge * 255));
        var rim = new RectF(bounds.Left + Dp(0.5f), bounds.Top + Dp(0.5f), bounds.Right - Dp(0.5f), bounds.Bottom - Dp(0.5f));
        canvas.DrawRoundRect(rim, radius, radius, edge);

        // The colour thins over the last tenth, where the stroke lifted: taken out of the layer, not painted over.
        var start = bounds.Right - (bounds.Width() * 0.1f);
        thin.SetShader(new LinearGradient(
            start, 0, bounds.Right, 0,
            new Color(0, 0, 0, 0), new Color(0, 0, 0, (int)Math.Round(MaterialStrength.MarkerEndThins * 255)),
            Shader.TileMode.Clamp!));
        thin.SetXfermode(new PorterDuffXfermode(PorterDuff.Mode.DstOut!));
        canvas.DrawRect(start, bounds.Top, bounds.Right, bounds.Bottom, thin);

        canvas.RestoreToCount(layer);

        if (Pressed)
        {
            rule.SetStyle(Paint.Style.Stroke);
            rule.StrokeWidth = Dp(1);
            rule.Color = pressedRule;
            var inset = new RectF(bounds.Left + Dp(2), bounds.Top + Dp(2), bounds.Right - Dp(2), bounds.Bottom - Dp(2));
            canvas.DrawRoundRect(inset, Math.Max(0, radius - Dp(2)), Math.Max(0, radius - Dp(2)), rule);
        }
    }
}

/// <summary>
/// A colored-pencil outline (DESIGN.md §2): the secondary button, a chip. The stroke has the pencil's tooth, and so does
/// the fill when there is one (a chip that is on). Held, the tooth rises by half (§8).
/// </summary>
public sealed class PencilOutlineDrawable(Color stroke, Color? fill, float radiusDp, float strokeDp, float density) : MaterialDrawable(density)
{
    private readonly Paint line = new(PaintFlags.AntiAlias);
    private readonly Paint face = new(PaintFlags.AntiAlias);

    public override void Draw(Canvas canvas)
    {
        var bounds = new RectF(Bounds);
        if (bounds.Width() <= 0 || bounds.Height() <= 0)
        {
            return;
        }
        var half = Dp(strokeDp) / 2;
        var box = new RectF(bounds.Left + half, bounds.Top + half, bounds.Right - half, bounds.Bottom - half);
        var radius = Math.Max(0, Dp(radiusDp) - half);

        if (fill is { } colour)
        {
            MaterialPainters.Pencil(face, colour, Density, Pressed);
            face.SetStyle(Paint.Style.Fill);
            canvas.DrawRoundRect(box, radius, radius, face);
        }

        MaterialPainters.Pencil(line, stroke, Density, Pressed);
        line.SetStyle(Paint.Style.Stroke);
        line.StrokeWidth = Dp(strokeDp);
        canvas.DrawRoundRect(box, radius, radius, line);
    }
}

/// <summary>
/// A highlighter band (DESIGN.md §2): the selected row, the segmented control's choice. The hue's wash behind
/// full-strength ink, streaked along its length, its top and bottom wandering gently, chisel-cut at the ends where
/// the colour pools. It sits behind the ink, never over it.
/// </summary>
public sealed class HighlighterDrawable(Color hue, float density) : MaterialDrawable(density)
{
    private const float MarginDp = 2;
    private const float ChiselDp = 6;
    private const float WanderDp = 1;

    private readonly Paint band = new(PaintFlags.AntiAlias);
    private readonly Paint pool = new(PaintFlags.AntiAlias);
    private Path? outline;

    protected override void OnBoundsChange(Rect bounds)
    {
        base.OnBoundsChange(bounds);
        outline = Outline(new RectF(bounds));
    }

    public override void Draw(Canvas canvas)
    {
        var bounds = new RectF(Bounds);
        if (bounds.Width() <= 0 || bounds.Height() <= 0)
        {
            return;
        }
        outline ??= Outline(bounds);

        band.Color = new Color((int)hue.R, (int)hue.G, (int)hue.B, 255);
        band.SetShader(MaterialShaders.Tiled(MaterialShaders.HighlighterStreaks, MaterialStrength.StreakRowDp * Density));
        canvas.DrawPath(outline, band);

        // Where the nib started and lifted, the colour pools to the pool strength over the wash.
        var save = canvas.Save();
        canvas.ClipPath(outline);
        var reach = (float)Math.Max(Dp(8), bounds.Width() * 0.05f);
        var extra = (int)Math.Round((MaterialStrength.HighlighterPool * 255) - MaterialStrength.WashAlpha);
        var pooled = new Color((int)hue.R, (int)hue.G, (int)hue.B, Math.Max(0, extra));
        var clear = new Color((int)hue.R, (int)hue.G, (int)hue.B, 0);
        pool.SetShader(new LinearGradient(bounds.Left, 0, bounds.Left + reach, 0, pooled, clear, Shader.TileMode.Clamp!));
        canvas.DrawRect(bounds.Left, bounds.Top, bounds.Left + reach, bounds.Bottom, pool);
        pool.SetShader(new LinearGradient(bounds.Right - reach, 0, bounds.Right, 0, clear, pooled, Shader.TileMode.Clamp!));
        canvas.DrawRect(bounds.Right - reach, bounds.Top, bounds.Right, bounds.Bottom, pool);
        canvas.RestoreToCount(save);
    }

    // The band's shape: inset a little from the row, the long edges wandering on slow sines, and the ends cut on a
    // slant as a chisel nib leaves them.
    private Path Outline(RectF bounds)
    {
        var path = new Path();
        var left = bounds.Left + Dp(MarginDp);
        var right = bounds.Right - Dp(MarginDp);
        var top = bounds.Top + Dp(MarginDp);
        var bottom = bounds.Bottom - Dp(MarginDp);
        var chisel = Math.Min(Dp(ChiselDp), (right - left) / 4);
        var wander = Dp(WanderDp);
        var step = Math.Max(Dp(4), 1);

        path.MoveTo(left + chisel, top);
        for (var x = left + chisel; x <= right; x += step)
        {
            path.LineTo(x, top + (wander * (float)(Math.Sin((x / Dp(27)) + 0.7) * 0.7 + Math.Sin((x / Dp(11)) + 2.1) * 0.3)));
        }
        path.LineTo(right, top);
        path.LineTo(right - chisel, bottom);
        for (var x = right - chisel; x >= left; x -= step)
        {
            path.LineTo(x, bottom + (wander * (float)(Math.Sin((x / Dp(31)) + 1.9) * 0.7 + Math.Sin((x / Dp(13)) + 0.4) * 0.3)));
        }
        path.LineTo(left, bottom);
        path.Close();
        return path;
    }
}

/// <summary>
/// A card or field on paper (DESIGN.md §2): its ground with the paper grain when the ground is paper, an optional
/// hairline (solid, or dashed for a place still empty), rounded corners. Held, the grain deepens by half (§8).
/// A ground that is not paper (an ink pill, an identity track) is drawn flat, as the grain belongs to paper only.
/// </summary>
public sealed class PaperCardDrawable(Color ground, bool grained, Color? stroke, float radiusDp, float strokeDp, float dashDp, float density)
    : MaterialDrawable(density)
{
    private readonly Paint face = new(PaintFlags.AntiAlias);
    private readonly Paint flecks = new(PaintFlags.AntiAlias);
    private readonly Paint line = new(PaintFlags.AntiAlias);

    public override void Draw(Canvas canvas)
    {
        var bounds = new RectF(Bounds);
        if (bounds.Width() <= 0 || bounds.Height() <= 0)
        {
            return;
        }
        var radius = Dp(radiusDp);

        face.Color = ground;
        canvas.DrawRoundRect(bounds, radius, radius, face);

        if (grained)
        {
            // One grain texel a device pixel, as every paper ground is.
            flecks.SetShader(MaterialShaders.Tiled(Pressed ? MaterialShaders.PaperGrainPressed : MaterialShaders.PaperGrain, 1f));
            canvas.DrawRoundRect(bounds, radius, radius, flecks);
        }

        if (stroke is { } colour)
        {
            var half = Dp(strokeDp) / 2;
            var box = new RectF(bounds.Left + half, bounds.Top + half, bounds.Right - half, bounds.Bottom - half);
            line.SetStyle(Paint.Style.Stroke);
            line.StrokeWidth = Dp(strokeDp);
            line.Color = colour;
            line.SetPathEffect(dashDp > 0 ? new DashPathEffect([Dp(dashDp), Dp(dashDp)], 0) : null);
            canvas.DrawRoundRect(box, Math.Max(0, radius - half), Math.Max(0, radius - half), line);
        }
    }
}
