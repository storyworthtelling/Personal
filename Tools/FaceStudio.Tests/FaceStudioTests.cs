using System.Xml.Linq;
using Watch.Domain.Design;
using Watch.Domain.Face;

namespace FaceStudio.Tests;

/// <summary>P14 W-2.1.1, W-2.1.2: the face is study 1b (A46), time and date only (A34), no taps (A35, TF §3.4).</summary>
public class FaceStudioTests
{
    private const double Close = 0.01;

    [Fact]
    public void EveryHourHasItsShape()
    {
        for (var hour = 0; hour < 24; hour++)
        {
            var shape = HourShape.For(new TimeOnly(hour, 0));
            var group = FaceRead.Hour("lit", hour);
            var rim = FaceRead.Part(group, "rim");

            if (shape.Sides == 1)
            {
                var dot = rim.Elements("Ellipse").Single();
                Assert.Equal(2 * FaceGeometry.DotRadius * FaceRead.Radius, FaceRead.Number(dot, "width"), Close);
                Assert.Empty(group.Descendants("PartImage"));
            }
            else
            {
                var lines = rim.Elements("Line").ToList();
                Assert.Equal(shape.Sides == 2 ? 1 : shape.Sides, lines.Count);
                AssertOutline(shape, FaceGeometry.ShapeRadius(shape), lines);
                if (shape.Sides >= 3)
                {
                    var image = group.Descendants("Image").Single();
                    Assert.Equal(Masks.Name(shape.Sides, shape.Afternoon), (string)image.Attribute("resource")!);
                    Assert.Equal("MASK", (string)image.Parent!.Attribute("renderMode")!);
                }
            }

            var rings = group.Descendants().Where(element => (string?)element.Attribute("name") == "ring").ToList();
            Assert.Equal(shape.Afternoon ? 1 : 0, rings.Count);
            if (shape.Afternoon && shape.Sides >= 3)
            {
                AssertOutline(shape, FaceGeometry.RingRadius, rings[0].Elements("Line").ToList());
            }
            else if (shape.Afternoon)
            {
                var circle = rings[0].Elements("Ellipse").Single();
                Assert.Equal(2 * FaceGeometry.RingRadius * FaceRead.Radius, FaceRead.Number(circle, "width"), Close);
            }
        }
    }

    [Fact]
    public void ColourFollowsTheMinute()
    {
        var glass = FaceRead.Hour("lit", 7).Descendants("Fill").First(fill => fill.Element("Transform") is not null);
        var rim = FaceRead.Part(FaceRead.Hour("lit", 7), "rim").Descendants("Stroke").First();
        var ring = FaceRead.Part(FaceRead.Hour("lit", 19), "ring").Descendants("Stroke").First();

        Assert.Equal(FaceRead.Expected(FaceColour.Glass), FaceRead.MinuteColours(glass));
        Assert.Equal(FaceRead.Expected(FaceColour.Rim), FaceRead.MinuteColours(rim));
        Assert.Equal(FaceRead.Expected(FaceColour.Ring), FaceRead.MinuteColours(ring));
    }

    [Fact]
    public void AlwaysOnIsOutlineWithoutDate()
    {
        var lit = FaceRead.Layer("lit");
        var always = FaceRead.Layer("always-on");

        AssertAmbient(lit, "0");
        AssertAmbient(always, "255");
        Assert.Equal("0", (string)always.Attribute("alpha")!);

        Assert.Empty(always.Descendants("Fill"));
        Assert.Empty(always.Descendants("PartImage"));
        Assert.Empty(always.Descendants("PartText"));
        Assert.Empty(always.Descendants("RadialGradient"));
        Assert.Single(lit.Descendants("PartText"));

        for (var hour = 0; hour < 24; hour++)
        {
            var shape = HourShape.For(new TimeOnly(hour, 0));
            var outline = FaceRead.Part(FaceRead.Hour("always-on", hour), "outline");
            var stroke = outline.Descendants("Stroke").First();
            var width = shape.Sides == 2 ? FaceGeometry.AlwaysOnLineStroke : FaceGeometry.AlwaysOnStroke;
            Assert.Equal(width * FaceRead.Radius, FaceRead.Number(stroke, "thickness"), Close);
            Assert.Equal(FaceRead.Expected(FaceColour.AlwaysOn), FaceRead.MinuteColours(stroke));
            if (shape.Sides >= 2)
            {
                AssertOutline(shape, FaceGeometry.ShapeRadius(shape), outline.Elements("Line").ToList());
            }
        }

        var ring = FaceRead.Part(FaceRead.Hour("always-on", 16), "ring").Descendants("Stroke").First();
        Assert.Equal(FaceRead.Expected(FaceColour.AlwaysOnRing), FaceRead.MinuteColours(ring));
    }

    [Fact]
    public void TheDateIsWeekdayAndDay()
    {
        var date = FaceRead.Scene.Descendants("PartText").Single();
        var font = date.Descendants("Font").Single();
        var template = font.Element("Upper")!.Element("Template")!;

        Assert.Equal(PointFace.FontResource, (string)font.Attribute("family")!);
        Assert.Equal(WatchTokens.Face.DateSize * FaceRead.Radius, FaceRead.Number(font, "size"), Close);
        Assert.Equal(["[DAY_OF_WEEK_S]", "[DAY]"], template.Elements("Parameter").Select(item => (string)item.Attribute("expression")!));
        Assert.Equal(FaceRead.Expected(FaceColour.Date), FaceRead.MinuteColours(font));

        var top = FaceRead.Number(date, "y");
        Assert.True(top > FaceRead.Radius * (1 + FaceGeometry.RingRadius), "The date sits under the shape and its ring.");
    }

    [Fact]
    public void NothingButTimeAndDate()
    {
        string[] allowed = ["HOUR_0_23", "MINUTE", "DAY", "DAY_OF_WEEK_S"];
        string[] refused =
        [
            "ComplicationSlot", "AnalogClock", "DigitalClock", "UserConfigurations", "BitmapFonts", "PartAnimatedImage", "Gyro",
        ];

        var sources = FaceRead.Face.Descendants()
            .SelectMany(element => element.Attributes().Select(attribute => attribute.Value)
                .Concat(element.Nodes().OfType<XText>().Select(text => text.Value)))
            .SelectMany(value => System.Text.RegularExpressions.Regex.Matches(value, @"\[([A-Z0-9_.]+)\]").Select(match => match.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.All(sources, source => Assert.Contains(source, allowed));
        Assert.All(refused, name => Assert.Empty(FaceRead.Face.Descendants(name)));
        Assert.Single(FaceRead.Face.Descendants("PartText"));
    }

    [Fact]
    public void NoTapTargetsOnTheFace()
    {
        Assert.Empty(FaceRead.Face.Descendants("Launch"));
        Assert.Empty(FaceRead.Face.Descendants("ComplicationSlot"));
        Assert.DoesNotContain(FaceRead.Face.Descendants().SelectMany(element => element.Attributes()),
            attribute => attribute.Value.Contains("TAP", StringComparison.Ordinal));
    }

    [Fact]
    public void TheFaceIsValidWff()
    {
        var problems = FaceSchema.Problems(FaceRead.Face, FaceRead.RepoRoot());

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheSchemaRefusesABrokenFace()
    {
        var broken = new XDocument(FaceRead.Face);
        broken.Root!.Element("Scene")!.Add(new XElement("Polygon"));

        Assert.NotEmpty(FaceSchema.Problems(broken, FaceRead.RepoRoot()));
    }

    private static void AssertAmbient(XElement layer, string alpha)
    {
        var variant = layer.Elements("Variant").Single();
        Assert.Equal("AMBIENT", (string)variant.Attribute("mode")!);
        Assert.Equal("alpha", (string)variant.Attribute("target")!);
        Assert.Equal(alpha, (string)variant.Attribute("value")!);
    }

    /// <summary>The lines run vertex to vertex round the shape (a line hour is its one line, top to bottom).</summary>
    private static void AssertOutline(HourShape shape, double radius, IReadOnlyList<XElement> lines)
    {
        var points = shape.Vertices(radius * FaceRead.Radius);
        for (var index = 0; index < lines.Count; index++)
        {
            var from = points[index];
            var to = points[(index + 1) % points.Count];
            Assert.Equal(FaceRead.Radius + from.X, FaceRead.Number(lines[index], "startX"), Close);
            Assert.Equal(FaceRead.Radius + from.Y, FaceRead.Number(lines[index], "startY"), Close);
            Assert.Equal(FaceRead.Radius + to.X, FaceRead.Number(lines[index], "endX"), Close);
            Assert.Equal(FaceRead.Radius + to.Y, FaceRead.Number(lines[index], "endY"), Close);
        }
    }
}
