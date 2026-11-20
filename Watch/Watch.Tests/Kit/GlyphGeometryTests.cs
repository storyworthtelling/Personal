using Watch.Domain.Kit;

namespace Watch.Tests.Kit;

/// <summary>WD §3 and the mock's <c>glyph</c>, <c>waveLine</c>, <c>edgeMarks</c>, <c>ring</c>: the shapes the kit paints.</summary>
public class GlyphGeometryTests
{
    [Fact]
    public void DashIsCentredAndShort()
    {
        Assert.Equal(1, GlyphGeometry.DashFrom + GlyphGeometry.DashTo, 9);
        Assert.Equal(0.4, GlyphGeometry.DashTo - GlyphGeometry.DashFrom, 9);
    }

    [Fact]
    public void DashStrokeIsFourteenPercent() => Assert.Equal(0.14, GlyphGeometry.DashStroke, 9);

    [Fact]
    public void WaveIsCentredOnTheFace()
    {
        Assert.Equal(1.6, WaveGeometry.Span, 9);
        Assert.Equal(0.2, WaveGeometry.LeftOn(1), 9);
    }

    [Fact]
    public void MarksSitOutsideTheWave()
    {
        var left = WaveGeometry.LeftOn(1);
        Assert.True(WaveGeometry.BackMark.To < left);
        Assert.True(WaveGeometry.SkipMark.From > left + WaveGeometry.Span);
        Assert.Equal(2, WaveGeometry.BackMark.From + WaveGeometry.SkipMark.To, 9);
    }

    [Fact]
    public void UndoRingHugsTheGlyph() => Assert.InRange(GlyphGeometry.UndoRingRadius, 0.5, 0.7);

    [Fact]
    public void RingSitsInsideTheEdge() => Assert.InRange(GlyphGeometry.RingRadius, 0.85, 0.97);
}
