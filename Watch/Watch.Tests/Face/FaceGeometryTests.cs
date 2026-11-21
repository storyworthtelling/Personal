using Watch.Domain.Design;
using Watch.Domain.Face;

namespace Watch.Tests.Face;

/// <summary>WD §2, §10; study 1b: where the shape, the after-noon ring and the date sit.</summary>
public class FaceGeometryTests
{
    [Fact]
    public void ShapeIsHalfTheFace()
    {
        Assert.Equal(0.5, FaceGeometry.ShapeRadius(HourShape.For(new TimeOnly(4, 20))));
        Assert.Equal(0.42, FaceGeometry.ShapeRadius(HourShape.For(new TimeOnly(16, 20))));
    }

    [Fact]
    public void RingIsAtTheApprovedRadius()
    {
        Assert.Equal(0.62, FaceGeometry.RingRadius);
    }

    [Fact]
    public void AfternoonShapeSitsInsideItsRing()
    {
        Assert.True(FaceGeometry.AfternoonRadius + FaceGeometry.RimStroke < FaceGeometry.RingRadius - FaceGeometry.RingStroke);
    }

    [Fact]
    public void DateSitsBelowEverything()
    {
        var dateTop = FaceGeometry.DateDrop - WatchTokens.Face.DateSize / 2;

        Assert.True(dateTop > FaceGeometry.RingRadius, "The date would touch the after-noon ring.");
        Assert.True(FaceGeometry.DateDrop + WatchTokens.Face.DateSize / 2 < 0.9, "The date would leave the round face.");
    }

    [Fact]
    public void AlwaysOnUnderFivePercent()
    {
        for (var hour = 0; hour < 24; hour++)
        {
            var lit = FaceGeometry.AlwaysOnLit(HourShape.For(new TimeOnly(hour, 0)));
            Assert.True(lit > 0, $"Nothing lit at {hour}:00.");
            Assert.True(lit < 0.05, $"{lit:P1} of the face lit at {hour}:00 (WD §2: under 5%).");
        }
    }

    [Fact]
    public void GlassLightsFromHighOnTheLeft()
    {
        Assert.True(FaceGeometry.HighlightX < 0);
        Assert.True(FaceGeometry.HighlightY < 0);
        Assert.True(FaceGeometry.GlassReach > 1);
    }
}
