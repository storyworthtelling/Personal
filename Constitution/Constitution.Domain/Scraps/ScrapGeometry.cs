namespace Constitution.Domain.Scraps;

/// <summary>
/// The shapes scraps and their tape occupy once turned, and whether two of them meet. A tilted
/// scrap is judged by the polygon it really covers, not by the axis-aligned box around it: the
/// box would call two neighbours overlapping that merely lean towards each other.
/// </summary>
public static class ScrapGeometry
{
    /// <summary>
    /// The four corners of a box turned <paramref name="tiltDegrees"/> about its own centre,
    /// clockwise on screen when positive (y grows downward, as CSS <c>rotate()</c> turns), in the
    /// order top-left, top-right, bottom-right, bottom-left.
    /// </summary>
    public static ScrapPoint[] Corners(double x, double y, double width, double height, double tiltDegrees)
    {
        var centreX = x + (width / 2);
        var centreY = y + (height / 2);
        var radians = tiltDegrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

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

    public static ScrapPoint[] BodyCorners(PlacedScrap scrap) =>
        Corners(scrap.X, scrap.Y, scrap.Width, scrap.Height, scrap.TiltDegrees);

    public static ScrapPoint[] TapeCorners(TapeAnchor tape) =>
        Corners(tape.CenterX - (tape.WidthDp / 2), tape.CenterY - (tape.HeightDp / 2),
            tape.WidthDp, tape.HeightDp, tape.RotationDegrees);

    /// <summary>
    /// Whether two convex polygons share any point, by the separating-axis test: they are apart
    /// exactly when some edge's normal has the two shapes' shadows on it not overlapping. Edges
    /// that only touch count as overlapping, because the page's rule is that scraps never touch.
    /// </summary>
    public static bool Overlaps(IReadOnlyList<ScrapPoint> a, IReadOnlyList<ScrapPoint> b)
    {
        if (a.Count < 3 || b.Count < 3)
        {
            throw new ArgumentException("A polygon needs at least three corners.");
        }

        return !HasSeparatingEdge(a, b) && !HasSeparatingEdge(b, a);
    }

    private static bool HasSeparatingEdge(IReadOnlyList<ScrapPoint> edgesOf, IReadOnlyList<ScrapPoint> other)
    {
        for (var i = 0; i < edgesOf.Count; i++)
        {
            var from = edgesOf[i];
            var to = edgesOf[(i + 1) % edgesOf.Count];
            var normalX = -(to.Y - from.Y);
            var normalY = to.X - from.X;

            Shadow(edgesOf, normalX, normalY, out var minA, out var maxA);
            Shadow(other, normalX, normalY, out var minB, out var maxB);
            if (maxA < minB || maxB < minA)
            {
                return true;
            }
        }

        return false;
    }

    private static void Shadow(IReadOnlyList<ScrapPoint> polygon, double normalX, double normalY, out double min, out double max)
    {
        min = double.PositiveInfinity;
        max = double.NegativeInfinity;
        foreach (var point in polygon)
        {
            var along = (point.X * normalX) + (point.Y * normalY);
            min = Math.Min(min, along);
            max = Math.Max(max, along);
        }
    }
}
