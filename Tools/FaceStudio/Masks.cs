using Watch.Domain.Design;
using Watch.Domain.Face;

namespace FaceStudio;

/// <summary>Where a shape's mask sits on the face, in Watch Face Format units (a pixel of the mask each).</summary>
public readonly record struct MaskBox(int X, int Y, int Side);

/// <summary>
/// A filled polygon's silhouette. The format draws lines, arcs, rectangles and ellipses but fills no
/// polygon, so each hour's glass is a rectangle in the minute's colour drawn through this mask.
/// </summary>
public static class Masks
{
    /// <summary>Room round the shape, so its smoothed edge is not cut by the image's own.</summary>
    private const int Margin = 3;

    /// <summary>The mask's resource name: which half of the day and how many sides.</summary>
    public static string Name(int sides, bool afternoon) => (afternoon ? "afternoon" : "morning") + sides;

    /// <summary>The square round a shape of <paramref name="radius"/> units, centred on the face.</summary>
    public static MaskBox Box(double radius)
    {
        var half = (int)Math.Ceiling(radius) + Margin;
        var corner = PointFace.Canvas / 2 - half;
        return new MaskBox(corner, corner, 2 * half);
    }

    /// <summary>The silhouette of a <paramref name="sides"/>-sided hour shape (three or more) on its box.</summary>
    public static Raster Draw(int sides, double radius)
    {
        if (sides < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(sides), sides, "A dot and a line have no inside to mask.");
        }

        var box = Box(radius);
        var centre = PointFace.Canvas / 2.0 - box.X;
        var points = new HourShape(sides, Afternoon: false, Hue: 0).Vertices(radius)
            .Select(point => new ShapePoint(centre + point.X, centre + point.Y))
            .ToList();
        var raster = new Raster(box.Side, box.Side);
        var solid = Srgb.Argb(WatchTokens.Face.Highlight);
        raster.Paint((x, y) => Inside(points, x, y), solid);
        return raster;
    }

    /// <summary>Inside a convex polygon whose points run clockwise on the screen (y down).</summary>
    public static bool Inside(IReadOnlyList<ShapePoint> points, double x, double y)
    {
        for (var index = 0; index < points.Count; index++)
        {
            var from = points[index];
            var to = points[(index + 1) % points.Count];
            if ((to.X - from.X) * (y - from.Y) - (to.Y - from.Y) * (x - from.X) < 0)
            {
                return false;
            }
        }
        return true;
    }
}
