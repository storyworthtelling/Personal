using Constitution.Domain.Scraps;

namespace Constitution.Tests.Scraps;

public class ScrapGeometryTests
{
    private static ScrapPoint[] Box(double x, double y, double w, double h, double tilt) =>
        ScrapGeometry.Corners(x, y, w, h, tilt);

    [Fact]
    public void CornersTurnAboutTheCentreClockwiseOnScreen()
    {
        // A 10 x 4 box centred on (5, 2), turned a quarter: the long side now runs down the page.
        var corners = Box(0, 0, 10, 4, 90);

        Assert.Equal(new[] { (5.0 + 2, 2.0 - 5), (5.0 + 2, 2.0 + 5), (5.0 - 2, 2.0 + 5), (5.0 - 2, 2.0 - 5) },
            corners.Select(c => (Math.Round(c.X, 9), Math.Round(c.Y, 9))).ToArray());
    }

    [Fact]
    public void NoTiltIsTheBoxItself()
    {
        var corners = Box(10, 20, 30, 40, 0);

        Assert.Equal(new ScrapPoint(10, 20), corners[0]);
        Assert.Equal(new ScrapPoint(40, 60), corners[2]);
    }

    [Fact]
    public void SeparatedBoxesDoNotOverlapAndNearbyOnesDo()
    {
        Assert.False(ScrapGeometry.Overlaps(Box(0, 0, 100, 50, 1), Box(0, 60, 100, 50, -1)));
        Assert.True(ScrapGeometry.Overlaps(Box(0, 0, 100, 50, 1), Box(0, 40, 100, 50, -1)));
    }

    [Fact]
    public void BoxesThatOnlyTouchCountAsOverlapping()
    {
        // "Never touching another" - an edge shared exactly is a failure, not a pass.
        Assert.True(ScrapGeometry.Overlaps(Box(0, 0, 100, 50, 0), Box(0, 50, 100, 50, 0)));
    }

    [Fact]
    public void ItIsTheTurnedShapeNotItsAxisAlignedHull()
    {
        // Two long thin strips on the same 45 degree diagonal, side by side. Their hulls are big
        // squares that overlap heavily; the strips themselves never meet (perpendicular gap of
        // about 14 against a thickness of 10).
        var a = Box(-50, -5, 100, 10, 45);
        var b = Box(-50, 15, 100, 10, 45);

        Assert.False(ScrapGeometry.Overlaps(a, b));
        Assert.True(HullsOverlap(a, b), "The case is only meaningful if the hulls do overlap.");
        // ...and the same strips closer than their thickness do meet.
        Assert.True(ScrapGeometry.Overlaps(a, Box(-50, 3, 100, 10, 45)));
    }

    [Fact]
    public void ItAgreesWithAnIndependentJudgeOnRandomPairs()
    {
        var random = new Random(4242);
        var overlapping = 0;
        var apart = 0;
        for (var i = 0; i < 20000; i++)
        {
            var a = Box(random.NextDouble() * 200, random.NextDouble() * 200,
                20 + (random.NextDouble() * 150), 10 + (random.NextDouble() * 100), (random.NextDouble() * 180) - 90);
            var b = Box(random.NextDouble() * 200, random.NextDouble() * 200,
                20 + (random.NextDouble() * 150), 10 + (random.NextDouble() * 100), (random.NextDouble() * 180) - 90);

            var expected = ScrapOracle.Touch(a, b);
            Assert.Equal(expected, ScrapGeometry.Overlaps(a, b));
            if (expected)
            {
                overlapping++;
            }
            else
            {
                apart++;
            }
        }

        // Both outcomes occurred in quantity, so agreement is not agreement about nothing.
        Assert.True(overlapping > 2000 && apart > 2000, $"{overlapping} overlapping, {apart} apart.");
    }

    [Fact]
    public void PlacedScrapAndTapeCornersMatchTheirBoxes()
    {
        var tape = new TapeAnchor(50, 10, 78, 20, 4);
        var scrap = new PlacedScrap("s", ScrapKind.Receipt, 10, 20, 200, 80, -1, tape);

        Assert.Equal(4, ScrapGeometry.BodyCorners(scrap).Length);
        Assert.Equal(ScrapGeometry.Corners(10, 20, 200, 80, -1), ScrapGeometry.BodyCorners(scrap));
        Assert.Equal(ScrapGeometry.Corners(50 - 39, 10 - 10, 78, 20, 4), ScrapGeometry.TapeCorners(tape));
    }

    private static bool HullsOverlap(ScrapPoint[] a, ScrapPoint[] b) =>
        a.Max(p => p.X) > b.Min(p => p.X) && b.Max(p => p.X) > a.Min(p => p.X)
        && a.Max(p => p.Y) > b.Min(p => p.Y) && b.Max(p => p.Y) > a.Min(p => p.Y);
}
