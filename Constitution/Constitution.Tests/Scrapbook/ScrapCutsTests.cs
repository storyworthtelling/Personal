using Constitution.Domain.Scrapbook;
using Constitution.Domain.Scraps;

namespace Constitution.Tests.Scrapbook;

/// <summary>
/// C-3.2.3: the edges the mockups cut with clip-path (scissor, torn, serrated), transcribed as
/// data so the painter only walks a list. Counts and offsets are read off the mockup CSS, which
/// is what these tests pin.
/// </summary>
public class ScrapCutsTests
{
    private const double Width = 300;
    private const double Height = 120;

    [Theory]
    [InlineData(ScrapKind.MagazineClipping, 6)]
    [InlineData(ScrapKind.NotepadLeaf, 17)]
    [InlineData(ScrapKind.Receipt, 23)]
    [InlineData(ScrapKind.ChartScrap, 16)]
    [InlineData(ScrapKind.IndexCard, 0)]
    public void EachKindHasTheMockupsNumberOfCorners(ScrapKind kind, int expected)
    {
        Assert.Equal(expected, ScrapCuts.For(kind).Count);
    }

    [Fact]
    public void AnIndexCardIsNotCut()
    {
        Assert.Empty(ScrapCuts.Resolve(ScrapKind.IndexCard, Width, Height));
    }

    [Theory]
    [InlineData(ScrapKind.MagazineClipping)]
    [InlineData(ScrapKind.NotepadLeaf)]
    [InlineData(ScrapKind.Receipt)]
    [InlineData(ScrapKind.ChartScrap)]
    public void EveryCornerLiesInsideTheBox(ScrapKind kind)
    {
        foreach (var width in new[] { 200d, 300d, 372d })
        {
            foreach (var point in ScrapCuts.Resolve(kind, width, Height))
            {
                Assert.InRange(point.X, 0, width);
                Assert.InRange(point.Y, 0, Height);
            }
        }
    }

    [Theory]
    [InlineData(ScrapKind.MagazineClipping)]
    [InlineData(ScrapKind.NotepadLeaf)]
    [InlineData(ScrapKind.Receipt)]
    [InlineData(ScrapKind.ChartScrap)]
    public void ACutRemovesAnEdgeNotTheScrap(ScrapKind kind)
    {
        var area = PolygonArea(ScrapCuts.Resolve(kind, Width, Height));

        Assert.InRange(area, Width * Height * 0.95, (Width * Height) - 1);
    }

    [Fact]
    public void ThePolygonsRunInOneDirectionWithoutCrossing()
    {
        foreach (var kind in new[] { ScrapKind.MagazineClipping, ScrapKind.NotepadLeaf, ScrapKind.Receipt, ScrapKind.ChartScrap })
        {
            var points = ScrapCuts.Resolve(kind, Width, Height);

            Assert.True(PolygonArea(points) > 0, kind + " should wind the same way the mockup lists it");
        }
    }

    [Fact]
    public void TheReceiptIsSerratedOnlyAtTheFoot()
    {
        var points = ScrapCuts.Resolve(ScrapKind.Receipt, Width, Height);

        Assert.Equal(new ScrapPoint(0, 0), points[0]);
        Assert.Equal(new ScrapPoint(Width, 0), points[1]);
        // Every other corner is within the serration's 5 dp of the foot: teeth at the foot, valleys 5 dp up.
        Assert.All(points.Skip(2), p => Assert.InRange(p.Y, Height - 5, Height));
        Assert.Equal(10, points.Count(p => p.Y == Height));
        Assert.Equal(11, points.Count(p => p.Y == Height - 5));
    }

    [Fact]
    public void TheNotepadLeafIsTornOnlyAtTheTop()
    {
        var points = ScrapCuts.Resolve(ScrapKind.NotepadLeaf, Width, Height);

        Assert.All(points.Take(15), p => Assert.InRange(p.Y, 0, 4));
        Assert.Equal(new ScrapPoint(Width, Height), points[15]);
        Assert.Equal(new ScrapPoint(0, Height), points[16]);
        Assert.Equal(4, points[0].Y);
        Assert.Equal(0.04 * Width, points[1].X, 6);
    }

    [Fact]
    public void TheChartIsTornAtTheFootWithUnevenTeeth()
    {
        var points = ScrapCuts.Resolve(ScrapKind.ChartScrap, Width, Height);

        Assert.All(points.Skip(2), p => Assert.InRange(p.Y, Height - 6, Height));
        Assert.Contains(points, p => p.Y == Height - 6);
        Assert.Contains(points, p => p.Y == Height - 3);
        // Uneven: more than one depth below the foot.
        Assert.True(points.Skip(2).Select(p => p.Y).Distinct().Count() >= 4);
    }

    [Fact]
    public void TheClippingIsCutWithAWanderingScissor()
    {
        var points = ScrapCuts.Resolve(ScrapKind.MagazineClipping, Width, Height);

        Assert.Equal(new ScrapPoint(0, 1.5), points[0]);
        Assert.Equal(0.62 * Width, points[1].X, 6);
        Assert.Equal(0, points[1].Y);
        Assert.Equal(new ScrapPoint(Width, 1), points[2]);
        Assert.Equal(0.996 * Width, points[3].X, 6);
        Assert.Equal(Height - 0.5, points[4].Y, 6);
        Assert.Equal(Height - 1.5, points[5].Y, 6);
    }

    private static double PolygonArea(IReadOnlyList<ScrapPoint> points)
    {
        var sum = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return sum / 2;
    }
}
