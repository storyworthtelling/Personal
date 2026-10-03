namespace Codex.Domain.Design;

/// <summary>
/// The dog-ear's drawn geometry, in the units of mockup 17's own SVG (a 40 x 40 view box), so the MAUI
/// kit's painter and the native <c>FoldView</c> draw one shape and cannot disagree about where the
/// flap's tip or the arrow's arc is. Pure: it holds numbers and the arithmetic that turns the
/// mockup's SVG arc into points, and no drawing type.
///
/// <para>The hole is the triangle (0,0) (40,0) (40,40) filled with the fold's role; the flap is
/// (0,0) (40,40) <see cref="FlapTip"/> in paper — its tip lifts off the page rather than running to
/// the foot of the fold; the crease is the hairline between them. The back-turn arrow is two
/// <b>strokes</b>: an arc and a three-point head. It used to be the glyph "↶" set in Serif Semibold,
/// which put a font choice where a drawn mark belongs.</para>
/// </summary>
public static class DogEarGeometry
{
    /// <summary>The ear's size in dp, square (mockup 17: a 40px by 40px <c>.dv-ear</c>).</summary>
    public const int SizeDp = 40;

    /// <summary>The side of the view box the points below are written in.</summary>
    public const double ViewBox = 40.0;

    /// <summary>The loose tip of the turned-down flap, in view-box units (<c>L6 34</c>).</summary>
    public static readonly (double X, double Y) FlapTip = (6.0, 34.0);

    /// <summary>The arrow's stroke weight in view-box units; round caps and joins.</summary>
    public const double ArrowStroke = 1.6;

    /// <summary>The arrow arc's radius (<c>a5.2 5.2</c>).</summary>
    public const double ArrowRadius = 5.2;

    private static readonly (double X, double Y) ArcStart = (35.5, 13.5);
    private static readonly (double X, double Y) ArcEnd = (35.5 + -2.2, 13.5 + -6.9);

    /// <summary>The arc's centre: SVG's endpoint-to-centre conversion for large-arc 1, sweep 1.</summary>
    public static (double X, double Y) ArrowCenter { get; } = Center();

    /// <summary>
    /// How far the arc turns, in degrees: clockwise on a y-down screen, and more than half a turn
    /// (the mockup's large-arc flag) — the arrow is a nearly closed circle opening at the upper right.
    /// </summary>
    public static double ArrowSweepDegrees => Sweep() * 180.0 / Math.PI;

    /// <summary>The arc as <paramref name="segments"/> + 1 points from its start to its end.</summary>
    public static IReadOnlyList<(double X, double Y)> ArrowArc(int segments)
    {
        var centre = ArrowCenter;
        var start = Math.Atan2(ArcStart.Y - centre.Y, ArcStart.X - centre.X);
        var sweep = Sweep();

        var points = new List<(double X, double Y)>(segments + 1);
        for (var i = 0; i <= segments; i++)
        {
            var angle = start + (sweep * i / segments);
            points.Add((centre.X + (ArrowRadius * Math.Cos(angle)), centre.Y + (ArrowRadius * Math.Sin(angle))));
        }

        return points;
    }

    /// <summary>The arrow head: the arrow's tail end, the point, and the other wing (<c>M31 4.2 l2.6 2.4 -2.9 2</c>).</summary>
    public static IReadOnlyList<(double X, double Y)> ArrowHead { get; } =
    [
        (31.0, 4.2),
        (31.0 + 2.6, 4.2 + 2.4),
        (31.0 + 2.6 + -2.9, 4.2 + 2.4 + 2.0),
    ];

    /// <summary>A view-box length at the size the ear is actually drawn.</summary>
    public static float Scale(double unit, float size) => (float)(unit * size / ViewBox);

    private static (double X, double Y) Center()
    {
        // SVG 1.1 F.6.5 with rx = ry = r and no rotation; large-arc 1 and sweep 1 are equal flags, so
        // the centre sits on the far side of the chord: sign -1.
        var halfX = (ArcStart.X - ArcEnd.X) / 2;
        var halfY = (ArcStart.Y - ArcEnd.Y) / 2;
        var squared = (halfX * halfX) + (halfY * halfY);
        var coefficient = -Math.Sqrt(((ArrowRadius * ArrowRadius) - squared) / squared);

        return (
            (coefficient * halfY) + ((ArcStart.X + ArcEnd.X) / 2),
            (-coefficient * halfX) + ((ArcStart.Y + ArcEnd.Y) / 2));
    }

    private static double Sweep()
    {
        var centre = ArrowCenter;
        var first = Math.Atan2(ArcStart.Y - centre.Y, ArcStart.X - centre.X);
        var last = Math.Atan2(ArcEnd.Y - centre.Y, ArcEnd.X - centre.X);

        // Sweep-flag 1: the angle only grows.
        var delta = last - first;
        while (delta <= 0)
        {
            delta += 2 * Math.PI;
        }

        return delta;
    }
}
