using Watch.Domain.Face;

namespace Watch.Tests.Face;

public class HourShapeTests
{
    private const double Radius = 10;

    [Fact]
    public void OneIsADot()
    {
        var shape = HourShape.For(new TimeOnly(1, 0));

        Assert.Equal(1, shape.Sides);
        Assert.Equal([new ShapePoint(0, 0)], shape.Vertices(Radius));
    }

    [Fact]
    public void TwoIsALine()
    {
        var shape = HourShape.For(new TimeOnly(14, 5));
        var ends = shape.Vertices(Radius);

        Assert.Equal(2, shape.Sides);
        Assert.Equal(2, ends.Count);
        AssertAt(0, -Radius, ends[0]);
        AssertAt(0, Radius, ends[1]);
    }

    [Fact]
    public void FourIsADiamondOnItsPoint()
    {
        var points = HourShape.For(new TimeOnly(4, 20)).Vertices(Radius);

        Assert.Equal(4, points.Count);
        AssertAt(0, -Radius, points[0]);
        AssertAt(Radius, 0, points[1]);
        AssertAt(0, Radius, points[2]);
        AssertAt(-Radius, 0, points[3]);
    }

    [Fact]
    public void TwelveHasTwelveSides()
    {
        Assert.Equal(12, HourShape.For(new TimeOnly(0, 0)).Sides);
        Assert.Equal(12, HourShape.For(new TimeOnly(12, 30)).Sides);
        Assert.Equal(12, HourShape.For(new TimeOnly(12, 30)).Vertices(Radius).Count);
    }

    [Fact]
    public void VerticesLieOnTheRadius()
    {
        foreach (var point in HourShape.For(new TimeOnly(7, 0)).Vertices(Radius))
        {
            Assert.Equal(Radius, Math.Sqrt(point.X * point.X + point.Y * point.Y), 9);
        }
    }

    [Theory]
    [InlineData(13, 41, true)]
    [InlineData(11, 59, false)]
    [InlineData(12, 0, true)]
    [InlineData(0, 0, false)]
    [InlineData(23, 59, true)]
    public void AfternoonHasASecondRing(int hour, int minute, bool afternoon)
    {
        Assert.Equal(afternoon, HourShape.For(new TimeOnly(hour, minute)).Afternoon);
    }

    [Fact]
    public void MinuteSweepsTheWheel()
    {
        Assert.Equal(0, HourShape.For(new TimeOnly(9, 0)).Hue, 9);
        Assert.Equal(180, HourShape.For(new TimeOnly(9, 30)).Hue, 9);
        Assert.Equal(183, HourShape.For(new TimeOnly(9, 30, 30)).Hue, 9);
        Assert.Equal(354, HourShape.For(new TimeOnly(9, 59)).Hue, 9);
    }

    private static void AssertAt(double x, double y, ShapePoint point)
    {
        Assert.Equal(x, point.X, 9);
        Assert.Equal(y, point.Y, 9);
    }
}
