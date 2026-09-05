using Codex.Domain.Design;
using Xunit;

namespace Codex.Tests.Design;

public class DialGeometryTests
{
    private static readonly DialGeometry Tempo = new(Minimum: 90, Maximum: 180);

    [Fact]
    public void Fraction_AtMinimum_IsZero()
    {
        Assert.Equal(0d, Tempo.FractionOf(90), 6);
    }

    [Fact]
    public void Fraction_AtMaximum_IsOne()
    {
        Assert.Equal(1d, Tempo.FractionOf(180), 6);
    }

    [Fact]
    public void Fraction_AtMidpoint_IsAHalf()
    {
        Assert.Equal(0.5d, Tempo.FractionOf(135), 6);
    }

    // A dial that can be dragged past its own ends reports a sweep longer than its
    // track and paints the knob off the arc, so clamping is the control's job.
    [Fact]
    public void Fraction_BelowMinimum_ClampsToZero()
    {
        Assert.Equal(0d, Tempo.FractionOf(10), 6);
    }

    [Fact]
    public void Fraction_AboveMaximum_ClampsToOne()
    {
        Assert.Equal(1d, Tempo.FractionOf(400), 6);
    }

    [Fact]
    public void Angle_AtMinimum_StartsAtTheSweepOrigin()
    {
        Assert.Equal(DialGeometry.StartDegrees, Tempo.AngleOf(90), 6);
    }

    [Fact]
    public void Angle_AtMaximum_EndsAfterTheFullSweep()
    {
        Assert.Equal(DialGeometry.StartDegrees + DialGeometry.SweepDegrees, Tempo.AngleOf(180), 6);
    }

    // 270° leaves a quarter open at the bottom, so the track reads as a dial rather
    // than a ring and the gap sits where a thumb rests.
    [Fact]
    public void Sweep_LeavesAQuarterOpenAtTheBottom()
    {
        Assert.Equal(270d, DialGeometry.SweepDegrees);
        Assert.Equal(135d, DialGeometry.StartDegrees);
    }

    [Fact]
    public void ValueAtAngle_RoundTripsThroughAngleOf()
    {
        foreach (var value in new[] { 90, 112, 135, 158, 180 })
        {
            Assert.Equal(value, Tempo.ValueAtAngle(Tempo.AngleOf(value)));
        }
    }

    [Fact]
    public void ValueAtAngle_Rounds_SoDraggingLandsOnWholeUnits()
    {
        var justUnder = Tempo.AngleOf(120) - (DialGeometry.SweepDegrees / 90d * 0.4);

        Assert.Equal(120, Tempo.ValueAtAngle(justUnder));
    }

    [Fact]
    public void ValueAtAngle_BeforeTheSweep_ClampsToMinimum()
    {
        Assert.Equal(90, Tempo.ValueAtAngle(DialGeometry.StartDegrees - 30));
    }

    [Fact]
    public void ValueAtAngle_AfterTheSweep_ClampsToMaximum()
    {
        Assert.Equal(180, Tempo.ValueAtAngle(DialGeometry.StartDegrees + DialGeometry.SweepDegrees + 30));
    }

    // The knob rides the track, so its centre is the arc point at the value's angle.
    [Fact]
    public void KnobAt_Minimum_SitsAtTheLowerLeft()
    {
        var (x, y) = Tempo.KnobAt(90, centreX: 100, centreY: 100, radius: 80);

        Assert.Equal(100 - 80 * Math.Sqrt(0.5), x, 3);
        Assert.Equal(100 + 80 * Math.Sqrt(0.5), y, 3);
    }

    [Fact]
    public void KnobAt_Midpoint_SitsDirectlyAboveTheCentre()
    {
        var (x, y) = Tempo.KnobAt(135, centreX: 100, centreY: 100, radius: 80);

        Assert.Equal(100, x, 3);
        Assert.Equal(20, y, 3);
    }

    [Fact]
    public void KnobAt_Maximum_SitsAtTheLowerRight()
    {
        var (x, y) = Tempo.KnobAt(180, centreX: 100, centreY: 100, radius: 80);

        Assert.Equal(100 + 80 * Math.Sqrt(0.5), x, 3);
        Assert.Equal(100 + 80 * Math.Sqrt(0.5), y, 3);
    }

    [Fact]
    public void Constructing_WithMaximumNotAboveMinimum_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DialGeometry(120, 120));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DialGeometry(120, 90));
    }

    // Where a finger is, in the view's own coordinates (y grows downward), and what
    // value that means. The sweep runs clockwise from the lower left.
    [Fact]
    public void ValueAtPoint_AtTheStartOfTheTrack_IsTheMinimum()
    {
        Assert.Equal(90, Tempo.ValueAtPoint(-10, 10));
    }

    [Fact]
    public void ValueAtPoint_AtTheEndOfTheTrack_IsTheMaximum()
    {
        // 45° clockwise from three o'clock: the lower right, where the sweep ends.
        Assert.Equal(180, Tempo.ValueAtPoint(10, 10));
    }

    [Fact]
    public void ValueAtPoint_StraightUp_IsTheMidpoint()
    {
        Assert.Equal(135, Tempo.ValueAtPoint(0, -10));
    }

    // The open quarter at the bottom is not part of the track, so a finger that
    // wanders into it takes the end it is nearest rather than jumping across the dial.
    [Fact]
    public void ValueAtPoint_InTheGap_TakesTheNearerEnd()
    {
        Assert.Equal(180, Tempo.ValueAtPoint(5, 10));   // below the end of the sweep
        Assert.Equal(90, Tempo.ValueAtPoint(-5, 10));   // below its start
    }

    [Fact]
    public void ValueAtPoint_AtTheCentre_IsTheMinimum()
    {
        // No direction to read; the value cannot be changed by a tap on the pivot.
        Assert.Equal(90, Tempo.ValueAtPoint(0, 0));
    }
}
