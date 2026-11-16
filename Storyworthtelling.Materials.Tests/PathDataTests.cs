namespace Storyworthtelling.Materials.Tests;

public class PathDataTests
{
    [Fact]
    public void AbsoluteAndRelativeLinesLandWhereTheyShould()
    {
        var path = PathData.Parse("M3 6H13M17 6h4V8v2L3 12l2 2Z");

        Assert.Equal(new PathCommand(PathVerb.Move, 3, 6), path[0]);
        Assert.Equal(new PathCommand(PathVerb.Line, 13, 6), path[1]);
        Assert.Equal(new PathCommand(PathVerb.Move, 17, 6), path[2]);
        Assert.Equal(new PathCommand(PathVerb.Line, 21, 6), path[3]);
        Assert.Equal(new PathCommand(PathVerb.Line, 21, 8), path[4]);
        Assert.Equal(new PathCommand(PathVerb.Line, 21, 10), path[5]);
        Assert.Equal(new PathCommand(PathVerb.Line, 3, 12), path[6]);
        Assert.Equal(new PathCommand(PathVerb.Line, 5, 14), path[7]);
        Assert.Equal(PathVerb.Close, path[8].Verb);
    }

    [Fact]
    public void ACurveAndItsSmoothSequelKeepTheirControlPoints()
    {
        var path = PathData.Parse("M5.6 5.6C8.5 9 8.5 15 5.6 18.4S2 20 1 21");

        Assert.Equal(new PathCommand(PathVerb.Cubic, 5.6f, 18.4f, 8.5f, 9, 8.5f, 15), path[1]);
        // The smooth curve's first control point reflects the previous second one about the current point.
        Assert.Equal(new PathCommand(PathVerb.Cubic, 1, 21, 2.7f, 21.8f, 2, 20), path[2]);
    }

    [Fact]
    public void ACircleWrittenAsTwoArcsComesBackRound()
    {
        var path = PathData.Parse("M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0");

        Assert.All(path.Where(c => c.Verb == PathVerb.Cubic), c =>
        {
            var radius = MathF.Sqrt((c.X - 12) * (c.X - 12) + (c.Y - 12) * (c.Y - 12));
            Assert.InRange(radius, 8.99f, 9.01f);
        });
        Assert.Equal(new PathCommand(PathVerb.Cubic, 3, 12), path.Last(c => c.Verb == PathVerb.Cubic) with { X1 = 0, Y1 = 0, X2 = 0, Y2 = 0 });
        Assert.True(path.Count(c => c.Verb == PathVerb.Cubic) >= 4, "a half circle is at least two quarter cubics");
    }

    [Fact]
    public void AnUnknownCommandIsRefusedWithItsLetter()
    {
        var failure = Assert.Throws<FormatException>(() => PathData.Parse("M3 3Q4 4 5 5"));
        Assert.Contains("'Q'", failure.Message, StringComparison.Ordinal);
    }
}
