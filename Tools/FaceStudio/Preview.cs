using Watch.Domain.Design;
using Watch.Domain.Face;

namespace FaceStudio;

/// <summary>
/// The face picker's preview (<c>res/drawable-nodpi/preview.png</c>): the face at
/// <see cref="PointFace.PreviewTime"/>, drawn from the same geometry and colours as the format. The date is
/// left out, since this tool sets no type; the watch draws the face itself once chosen.
/// </summary>
public static class Preview
{
    private const double Radius = PointFace.Canvas / 2.0;

    public static Raster Draw()
    {
        var shape = HourShape.For(PointFace.PreviewTime);
        var radius = FaceGeometry.ShapeRadius(shape) * Radius;
        var raster = new Raster(PointFace.Canvas, PointFace.Canvas);
        var ground = Srgb.Argb(WatchTokens.Ground);
        raster.Paint((x, y) => Near(x, y, Radius, Radius, Radius), ground);

        var points = Around(shape.Vertices(radius));
        if (shape.Sides == 1)
        {
            var dot = FaceGeometry.DotRadius * Radius;
            Glass(raster, shape.Hue, (x, y) => Near(x, y, Radius, Radius, dot), dot);
            raster.Paint((x, y) => Math.Abs(Distance(x, y, Radius, Radius) - dot) <= FaceGeometry.RimStroke * Radius / 2,
                Srgb.Argb(FaceColour.Rim(shape.Hue)));
        }
        else
        {
            if (shape.Sides >= 3)
            {
                Glass(raster, shape.Hue, (x, y) => Masks.Inside(points, x, y), radius);
            }
            var width = (shape.Sides == 2 ? FaceGeometry.LineStroke : FaceGeometry.RimStroke) * Radius;
            Edges(raster, points, shape.Sides == 2, width, Srgb.Argb(FaceColour.Rim(shape.Hue)));
        }

        if (shape.Afternoon)
        {
            var ring = Srgb.Argb(FaceColour.Ring(shape.Hue), WatchTokens.Face.RingAlpha);
            var width = FaceGeometry.RingStroke * Radius;
            if (shape.Sides <= 2)
            {
                var circle = FaceGeometry.RingRadius * Radius;
                raster.Paint((x, y) => Math.Abs(Distance(x, y, Radius, Radius) - circle) <= width / 2, ring);
            }
            else
            {
                Edges(raster, Around(shape.Vertices(FaceGeometry.RingRadius * Radius)), open: false, width, ring);
            }
        }

        return raster;
    }

    /// <summary>The glass: the body in the minute's colour, the highlight and shade laid over it as the format's gradient does.</summary>
    private static void Glass(Raster raster, double hue, Func<double, double, bool> inside, double radius)
    {
        var body = Srgb.Argb(FaceColour.Glass(hue));
        raster.Paint(inside, body);

        var lightX = Radius + FaceGeometry.HighlightX * radius;
        var lightY = Radius + FaceGeometry.HighlightY * radius;
        var reach = FaceGeometry.GlassReach * radius;
        raster.Paint(inside, (x, y) =>
        {
            var along = Distance(x, y, lightX, lightY) / reach;
            if (along < 0.5)
            {
                return Srgb.Argb(WatchTokens.Face.Highlight, WatchTokens.Face.HighlightAlpha * (1 - along / 0.5));
            }
            var shade = Math.Clamp((along - 0.6) / 0.4, 0, 1);
            return Srgb.Argb(WatchTokens.Face.Shade, WatchTokens.Face.ShadeAlpha * shade);
        });
    }

    private static void Edges(Raster raster, IReadOnlyList<ShapePoint> points, bool open, double width, int colour)
    {
        var edges = open ? points.Count - 1 : points.Count;
        raster.Paint((x, y) =>
        {
            for (var index = 0; index < edges; index++)
            {
                if (SegmentDistance(x, y, points[index], points[(index + 1) % points.Count]) <= width / 2)
                {
                    return true;
                }
            }
            return false;
        }, colour);
    }

    private static List<ShapePoint> Around(IReadOnlyList<ShapePoint> points) =>
        points.Select(point => new ShapePoint(Radius + point.X, Radius + point.Y)).ToList();

    private static bool Near(double x, double y, double centreX, double centreY, double radius) =>
        Distance(x, y, centreX, centreY) <= radius;

    private static double Distance(double x, double y, double toX, double toY) => Math.Sqrt((x - toX) * (x - toX) + (y - toY) * (y - toY));

    private static double SegmentDistance(double x, double y, ShapePoint from, ShapePoint to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = dx * dx + dy * dy;
        var along = length == 0 ? 0 : Math.Clamp(((x - from.X) * dx + (y - from.Y) * dy) / length, 0, 1);
        return Distance(x, y, from.X + along * dx, from.Y + along * dy);
    }
}
