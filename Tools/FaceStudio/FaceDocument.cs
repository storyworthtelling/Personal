using System.Globalization;
using System.Xml.Linq;
using Watch.Domain.Design;
using Watch.Domain.Face;

namespace FaceStudio;

/// <summary>
/// The face's <c>res/raw/watchface.xml</c> (W-2.1.1): study 1b exactly (A46, WD §10). For each hour of the
/// day a group, chosen by <c>[HOUR_0_23]</c>: the hour's shape from <see cref="HourShape"/>, its glass in the
/// minute's colour, and after noon its ring. Under it the date. In always-on, the shape and ring in outline
/// and no date (WD §2). Nothing else: no complication, no tap, no other data (A34, A35).
/// </summary>
public static class FaceDocument
{
    private const double Radius = PointFace.Canvas / 2.0;

    /// <summary>The glass's light: highlight to clear by half way, clear to shade from six tenths out.</summary>
    private const string GlassStops = "0 0.5 0.6 1";

    public static XDocument Build() =>
        new(new XElement("WatchFace",
            new XAttribute("width", PointFace.Canvas),
            new XAttribute("height", PointFace.Canvas),
            new XAttribute("clipShape", "CIRCLE"),
            new XElement("Metadata",
                new XAttribute("key", "PREVIEW_TIME"),
                new XAttribute("value", PointFace.PreviewTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture))),
            new XElement("Scene",
                new XAttribute("backgroundColor", FaceColours.Hex(WatchTokens.Ground)),
                Layer("lit", shownInAmbient: false, Hours(Lit), Date()),
                Layer("always-on", shownInAmbient: true, Hours(AlwaysOn)))));

    /// <summary>A whole-face group that shows only in interactive mode, or only in ambient (the format's own pattern).</summary>
    private static XElement Layer(string name, bool shownInAmbient, params object[] content)
    {
        var layer = Group(name, 0, 0, PointFace.Canvas);
        if (shownInAmbient)
        {
            layer.Add(new XAttribute("alpha", 0));
        }
        layer.Add(new XElement("Variant",
            new XAttribute("mode", "AMBIENT"),
            new XAttribute("target", "alpha"),
            new XAttribute("value", shownInAmbient ? 255 : 0)));
        layer.Add(content);
        return layer;
    }

    /// <summary>One group for each hour of the 24-hour day; only the hour's own is drawn.</summary>
    private static XElement Hours(Func<int, XElement> forHour)
    {
        var expressions = new XElement("Expressions");
        var condition = new XElement("Condition", expressions);
        for (var hour = 0; hour < 24; hour++)
        {
            var name = "hour" + hour;
            expressions.Add(new XElement("Expression", new XAttribute("name", name), $"[HOUR_0_23] == {hour}"));
            condition.Add(new XElement("Compare", new XAttribute("expression", name), forHour(hour)));
        }
        return condition;
    }

    private static XElement Lit(int hour)
    {
        var shape = HourShape.For(new TimeOnly(hour, 0));
        var radius = FaceGeometry.ShapeRadius(shape) * Radius;
        var group = Group("hour" + hour, 0, 0, PointFace.Canvas);

        if (shape.Sides == 1)
        {
            var dot = FaceGeometry.DotRadius * Radius;
            group.Add(Draw("glass", PointFace.Canvas,
                Circle(Radius, Radius, dot, Fill(FaceColour.Glass)),
                Circle(Radius, Radius, dot, Glass(Radius, Radius, dot))));
            group.Add(Draw("rim", PointFace.Canvas, Circle(Radius, Radius, dot, Stroke(FaceColour.Rim, FaceGeometry.RimStroke)())));
        }
        else if (shape.Sides == 2)
        {
            group.Add(Draw("rim", PointFace.Canvas, Outline(shape, radius, Stroke(FaceColour.Rim, FaceGeometry.LineStroke))));
        }
        else
        {
            var box = Masks.Box(radius);
            var centre = Radius - box.X;
            var glass = Group("glass", box.X, box.Y, box.Side);
            glass.Add(new XElement("PartImage",
                Geometry("silhouette", 0, 0, box.Side),
                new XAttribute("renderMode", "MASK"),
                new XElement("Image", new XAttribute("resource", Masks.Name(shape.Sides, shape.Afternoon)))));
            glass.Add(Draw("colour", box.Side,
                Rectangle(box.Side, Fill(FaceColour.Glass)),
                Rectangle(box.Side, Glass(centre, centre, radius))));
            group.Add(glass);
            group.Add(Draw("rim", PointFace.Canvas, Outline(shape, radius, Stroke(FaceColour.Rim, FaceGeometry.RimStroke))));
        }

        if (shape.Afternoon)
        {
            var ring = Ring(shape, Stroke(FaceColour.Ring, FaceGeometry.RingStroke));
            ring.Add(new XAttribute("alpha", (int)Math.Round(WatchTokens.Face.RingAlpha * 255)));
            group.Add(ring);
        }

        return group;
    }

    private static XElement AlwaysOn(int hour)
    {
        var shape = HourShape.For(new TimeOnly(hour, 0));
        var radius = FaceGeometry.ShapeRadius(shape) * Radius;
        var group = Group("hour" + hour, 0, 0, PointFace.Canvas);

        if (shape.Sides == 1)
        {
            group.Add(Draw("outline", PointFace.Canvas,
                Circle(Radius, Radius, FaceGeometry.DotRadius * Radius, Stroke(FaceColour.AlwaysOn, FaceGeometry.AlwaysOnStroke)())));
        }
        else
        {
            var width = shape.Sides == 2 ? FaceGeometry.AlwaysOnLineStroke : FaceGeometry.AlwaysOnStroke;
            group.Add(Draw("outline", PointFace.Canvas, Outline(shape, radius, Stroke(FaceColour.AlwaysOn, width))));
        }

        if (shape.Afternoon)
        {
            group.Add(Ring(shape, Stroke(FaceColour.AlwaysOnRing, FaceGeometry.RingStroke)));
        }

        return group;
    }

    /// <summary>The after-noon ring: the same shape at the ring's radius, or a circle round a dot or a line.</summary>
    private static XElement Ring(HourShape shape, Func<XElement> stroke) =>
        shape.Sides <= 2
            ? Draw("ring", PointFace.Canvas, Circle(Radius, Radius, FaceGeometry.RingRadius * Radius, stroke()))
            : Draw("ring", PointFace.Canvas, Outline(shape, FaceGeometry.RingRadius * Radius, stroke));

    /// <summary>The weekday and day ("SAT 31") under the shape, small, in the shape's colour at low saturation.</summary>
    private static XElement Date()
    {
        var size = WatchTokens.Face.DateSize * Radius;
        var height = (int)Math.Ceiling(2 * size);
        var top = (int)Math.Round(Radius + FaceGeometry.DateDrop * Radius - height / 2.0);
        return new XElement("PartText",
            Geometry("date", 0, top, PointFace.Canvas, height),
            new XElement("Text",
                new XAttribute("align", "CENTER"),
                new XElement("Font",
                    new XAttribute("family", PointFace.FontResource),
                    new XAttribute("size", Units(size)),
                    new XAttribute("weight", "MEDIUM"),
                    new XAttribute("color", FaceColours.AtTheHour(FaceColour.Date)),
                    ColourTransform(FaceColour.Date),
                    new XElement("Upper",
                        new XElement("Template",
                            "%s %s",
                            new XElement("Parameter", new XAttribute("expression", "[DAY_OF_WEEK_S]")),
                            new XElement("Parameter", new XAttribute("expression", "[DAY]")))))));
    }

    /// <summary>The shape's edges, vertex to vertex clockwise from twelve; a line hour is its one line.</summary>
    private static IEnumerable<XElement> Outline(HourShape shape, double radius, Func<XElement> stroke)
    {
        var points = shape.Vertices(radius);
        var edges = shape.Sides == 2 ? 1 : points.Count;
        for (var index = 0; index < edges; index++)
        {
            var from = points[index];
            var to = points[(index + 1) % points.Count];
            yield return new XElement("Line",
                new XAttribute("startX", Units(Radius + from.X)),
                new XAttribute("startY", Units(Radius + from.Y)),
                new XAttribute("endX", Units(Radius + to.X)),
                new XAttribute("endY", Units(Radius + to.Y)),
                stroke());
        }
    }

    private static Func<XElement> Stroke(Func<double, Oklch> tone, double width) => () =>
        new XElement("Stroke",
            new XAttribute("color", FaceColours.AtTheHour(tone)),
            new XAttribute("thickness", Units(width * Radius)),
            new XAttribute("cap", "ROUND"),
            ColourTransform(tone));

    private static XElement Fill(Func<double, Oklch> tone) =>
        new("Fill", new XAttribute("color", FaceColours.AtTheHour(tone)), ColourTransform(tone));

    /// <summary>The glass's light over the body: a highlight high on the left, the edge deeper (WD §2).</summary>
    private static XElement Glass(double centreX, double centreY, double radius)
    {
        var colours = string.Join(' ',
            FaceColours.Hex(WatchTokens.Face.Highlight, WatchTokens.Face.HighlightAlpha),
            FaceColours.Hex(WatchTokens.Face.Highlight, 0),
            FaceColours.Hex(WatchTokens.Face.Shade, 0),
            FaceColours.Hex(WatchTokens.Face.Shade, WatchTokens.Face.ShadeAlpha));
        return new XElement("Fill",
            new XAttribute("color", FaceColours.Hex(WatchTokens.Face.Highlight, 0)),
            new XElement("RadialGradient",
                new XAttribute("centerX", Units(centreX + FaceGeometry.HighlightX * radius)),
                new XAttribute("centerY", Units(centreY + FaceGeometry.HighlightY * radius)),
                new XAttribute("radius", Units(FaceGeometry.GlassReach * radius)),
                new XAttribute("colors", colours),
                new XAttribute("positions", GlassStops)));
    }

    private static XElement ColourTransform(Func<double, Oklch> tone) =>
        new("Transform", new XAttribute("target", "color"), new XAttribute("value", FaceColours.ByMinute(tone)));

    private static XElement Circle(double centreX, double centreY, double radius, XElement paint) =>
        new("Ellipse",
            new XAttribute("x", Units(centreX - radius)),
            new XAttribute("y", Units(centreY - radius)),
            new XAttribute("width", Units(2 * radius)),
            new XAttribute("height", Units(2 * radius)),
            paint);

    private static XElement Rectangle(int side, XElement fill) =>
        new("Rectangle", new XAttribute("x", 0), new XAttribute("y", 0), new XAttribute("width", side), new XAttribute("height", side), fill);

    private static XElement Draw(string name, int side, params object[] shapes) =>
        new("PartDraw", Geometry(name, 0, 0, side), shapes);

    private static XElement Group(string name, int x, int y, int side) => new("Group", Geometry(name, x, y, side));

    private static XAttribute[] Geometry(string name, int x, int y, int width, int? height = null) =>
    [
        new("name", name), new("x", x), new("y", y), new("width", width), new("height", height ?? width),
    ];

    private static string Units(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
}
