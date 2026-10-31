using Constitution.Domain.Scraps;

namespace Constitution.Tests.Scraps;

/// <summary>
/// An independent judge of "do these two convex polygons touch", for the layout tests. It is
/// deliberately a different method from the separating-axis test in <c>ScrapGeometry</c> (two
/// convex polygons meet exactly when a vertex of one lies inside the other, or an edge of one
/// crosses an edge of the other), and it builds its own rotated corners, so the layout is not
/// being marked by the same arithmetic that placed it.
/// </summary>
internal static class ScrapOracle
{
    public static ScrapPoint[] Rotated(double left, double top, double width, double height, double degrees)
    {
        var centreX = left + (width / 2);
        var centreY = top + (height / 2);
        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        // Clockwise on screen (y grows downward), as CSS rotate() turns.
        ScrapPoint Turn(double dx, double dy) =>
            new(centreX + (dx * cos) - (dy * sin), centreY + (dx * sin) + (dy * cos));

        return
        [
            Turn(-width / 2, -height / 2),
            Turn(width / 2, -height / 2),
            Turn(width / 2, height / 2),
            Turn(-width / 2, height / 2),
        ];
    }

    public static ScrapPoint[] Body(PlacedScrap scrap) =>
        Rotated(scrap.X, scrap.Y, scrap.Width, scrap.Height, scrap.TiltDegrees);

    public static ScrapPoint[] Tape(TapeAnchor tape) =>
        Rotated(tape.CenterX - (tape.WidthDp / 2), tape.CenterY - (tape.HeightDp / 2),
            tape.WidthDp, tape.HeightDp, tape.RotationDegrees);

    /// <summary>True when the two polygons share any point, touching included.</summary>
    public static bool Touch(ScrapPoint[] a, ScrapPoint[] b)
    {
        if (a.Any(point => Inside(b, point)) || b.Any(point => Inside(a, point)))
        {
            return true;
        }

        for (var i = 0; i < a.Length; i++)
        {
            for (var j = 0; j < b.Length; j++)
            {
                if (SegmentsMeet(a[i], a[(i + 1) % a.Length], b[j], b[(j + 1) % b.Length]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Inside(ScrapPoint[] polygon, ScrapPoint point)
    {
        var positive = false;
        var negative = false;
        for (var i = 0; i < polygon.Length; i++)
        {
            var cross = Cross(polygon[i], polygon[(i + 1) % polygon.Length], point);
            positive |= cross > 0;
            negative |= cross < 0;
        }

        return !(positive && negative);
    }

    private static double Cross(ScrapPoint from, ScrapPoint to, ScrapPoint point) =>
        ((to.X - from.X) * (point.Y - from.Y)) - ((to.Y - from.Y) * (point.X - from.X));

    private static bool SegmentsMeet(ScrapPoint p1, ScrapPoint p2, ScrapPoint p3, ScrapPoint p4)
    {
        var d1 = Math.Sign(Cross(p3, p4, p1));
        var d2 = Math.Sign(Cross(p3, p4, p2));
        var d3 = Math.Sign(Cross(p1, p2, p3));
        var d4 = Math.Sign(Cross(p1, p2, p4));
        return d1 != d2 && d3 != d4;
    }
}
