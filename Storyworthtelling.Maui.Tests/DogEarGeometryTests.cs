using System.Globalization;
using System.Text.RegularExpressions;
using Storyworthtelling.Maui.Layout;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// Finding 6: the ear's geometry is mockup 17's, member for member, read from the drawing's own SVG
/// at test time. <c>DogEarGeometry</c> is the pure half the MAUI painter and the native <c>FoldView</c>
/// both draw from, so the two cannot disagree about where the flap's tip or the arrow's arc is.
/// </summary>
public class DogEarGeometryTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void TheEarIsTheMockupsFortyDpSquare()
    {
        Assert.Equal(40, DogEarGeometry.SizeDp);
        Assert.Equal(40.0, DogEarGeometry.ViewBox);
        Assert.Matches(@"\.dv-ear\{[^}]*width:40px;height:40px", Mockup());
        Assert.Contains("viewBox=\"0 0 40 40\"", Ear(), StringComparison.Ordinal);
        Assert.Matches(@"\.dv-ear\{[^}]*top:0;right:66px", Mockup());
    }

    [Fact]
    public void TheFlapRunsFromTheCornerAlongTheCreaseToTheMockupsTip()
    {
        // <path d="M0 0 L40 40 L6 34 Z"> — the tip is NOT the foot of the fold (0,40): it lifts off.
        var flap = Regex.Match(Ear(), @"d=""M0 0 L40 40 L(?<x>[\d.]+) (?<y>[\d.]+) Z""");
        Assert.True(flap.Success, "The mockup's flap path is no longer 'M0 0 L40 40 L<x> <y> Z'.");

        Assert.Equal(Number(flap.Groups["x"]), DogEarGeometry.FlapTip.X, Tolerance);
        Assert.Equal(Number(flap.Groups["y"]), DogEarGeometry.FlapTip.Y, Tolerance);
    }

    [Fact]
    public void TheArrowArcStartsAndEndsWhereTheMockupsArcDoes()
    {
        // d="M35.5 13.5a5.2 5.2 0 1 1-2.2-6.9": start, radius, large-arc 1, sweep 1, relative end.
        var arc = Regex.Match(
            Ear(), @"d=""M(?<x>[\d.]+) (?<y>[\d.]+)a(?<r>[\d.]+) [\d.]+ 0 1 1(?<dx>-?[\d.]+)(?<dy>-?[\d.]+)""");
        Assert.True(arc.Success, "The mockup's arrow arc is no longer 'M<x> <y>a<r> <r> 0 1 1<dx><dy>'.");
        var start = (X: Number(arc.Groups["x"]), Y: Number(arc.Groups["y"]));
        var end = (X: start.X + Number(arc.Groups["dx"]), Y: start.Y + Number(arc.Groups["dy"]));
        var radius = Number(arc.Groups["r"]);

        var points = DogEarGeometry.ArrowArc(48);

        Assert.Equal(start.X, points[0].X, Tolerance);
        Assert.Equal(start.Y, points[0].Y, Tolerance);
        Assert.Equal(end.X, points[^1].X, Tolerance);
        Assert.Equal(end.Y, points[^1].Y, Tolerance);
        Assert.Equal(radius, DogEarGeometry.ArrowRadius, Tolerance);
    }

    [Fact]
    public void EveryPointOfTheArcLiesOnTheCircleAndTheArcIsTheLargeWayRound()
    {
        var center = DogEarGeometry.ArrowCenter;
        var points = DogEarGeometry.ArrowArc(48);

        foreach (var point in points)
        {
            var distance = Math.Sqrt(Math.Pow(point.X - center.X, 2) + Math.Pow(point.Y - center.Y, 2));
            Assert.Equal(DogEarGeometry.ArrowRadius, distance, 1e-6);
        }

        // large-arc-flag 1: more than half a turn; sweep-flag 1: clockwise on a y-down screen, so the
        // arc passes the bottom and the left of its circle and opens at the upper right.
        Assert.InRange(DogEarGeometry.ArrowSweepDegrees, 180.0, 359.0);
        Assert.True(points.Min(p => p.X) < center.X - DogEarGeometry.ArrowRadius + 0.05, "The arc never reaches the circle's left.");
        Assert.True(points.Max(p => p.Y) > center.Y + DogEarGeometry.ArrowRadius - 0.05, "The arc never reaches the circle's bottom.");
    }

    [Fact]
    public void TheArrowHeadIsTheMockupsThreePoints()
    {
        // d="M31 4.2l2.6 2.4-2.9 2": a start and two relative steps.
        var head = Regex.Match(
            Ear(), @"d=""M(?<x>[\d.]+) (?<y>[\d.]+)l(?<a>[\d.]+) (?<b>[\d.]+)(?<c>-?[\d.]+) (?<d>-?[\d.]+)""");
        Assert.True(head.Success, "The mockup's arrow head is no longer 'M<x> <y>l<a> <b><c> <d>'.");
        var p0 = (X: Number(head.Groups["x"]), Y: Number(head.Groups["y"]));
        var p1 = (X: p0.X + Number(head.Groups["a"]), Y: p0.Y + Number(head.Groups["b"]));
        var p2 = (X: p1.X + Number(head.Groups["c"]), Y: p1.Y + Number(head.Groups["d"]));

        var drawn = DogEarGeometry.ArrowHead;
        Assert.Equal(3, drawn.Count);
        Assert.Equal(p0.X, drawn[0].X, Tolerance);
        Assert.Equal(p0.Y, drawn[0].Y, Tolerance);
        Assert.Equal(p1.X, drawn[1].X, Tolerance);
        Assert.Equal(p1.Y, drawn[1].Y, Tolerance);
        Assert.Equal(p2.X, drawn[2].X, Tolerance);
        Assert.Equal(p2.Y, drawn[2].Y, Tolerance);
    }

    [Fact]
    public void TheArrowIsStrokedAtTheMockupsWeight()
    {
        var weights = Regex.Matches(Ear(), @"stroke-width=""(?<w>[\d.]+)""[^>]*stroke-linecap=""round""")
            .Select(m => Number(m.Groups["w"]))
            .Where(w => w > 1.2)
            .Distinct()
            .ToList();

        Assert.Equal([DogEarGeometry.ArrowStroke], weights);
    }

    [Fact]
    public void AViewBoxUnitScalesToTheSizeItIsDrawnAt()
    {
        Assert.Equal(20f, DogEarGeometry.Scale(20.0, 40f));
        Assert.Equal(10f, DogEarGeometry.Scale(40.0, 10f));
    }

    private static double Number(Group group) => double.Parse(group.Value, CultureInfo.InvariantCulture);

    private static string Mockup() => File.ReadAllText(RepoFile("Ledger/design/mockup-17.html"));

    /// <summary>The ear drawn in frame 2 (the undo): the one that carries the arrow.</summary>
    private static string Ear()
    {
        var match = Regex.Match(Mockup(), @"<svg class=""dv-ear"".*?</svg>", RegexOptions.Singleline);
        Assert.True(match.Success, "Mockup 17 no longer draws a dv-ear.");
        return match.Value;
    }

    private static string RepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
