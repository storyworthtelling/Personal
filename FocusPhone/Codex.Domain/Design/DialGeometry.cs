namespace Codex.Domain.Design;

/// <summary>
/// The maths behind DESIGN.md §7's Dial — the circular control for a single value,
/// added in P6 for Score's workout tempo. Pure so the arithmetic that decides where
/// a knob lands, and what value a drag means, is testable without a device; the view
/// stays a thin painter.
///
/// The track is a <b>270° sweep</b> starting at the lower left, which leaves a
/// quarter open at the bottom. That gap is what makes it read as a dial rather than
/// a ring, and it sits where a thumb rests rather than where the value does.
/// </summary>
public sealed record DialGeometry
{
    /// <summary>Where the track begins, in degrees clockwise from three o'clock.</summary>
    public const double StartDegrees = 135d;

    /// <summary>How far the track runs. A full circle would have no beginning.</summary>
    public const double SweepDegrees = 270d;

    public DialGeometry(int Minimum, int Maximum)
    {
        if (Maximum <= Minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(Maximum),
                $"A dial needs a range: maximum {Maximum} must exceed minimum {Minimum}.");
        }

        this.Minimum = Minimum;
        this.Maximum = Maximum;
    }

    public int Minimum { get; }

    public int Maximum { get; }

    /// <summary>How far along the track a value sits, 0–1. Clamped: a dial cannot be
    /// dragged past its own ends without painting the knob off the arc.</summary>
    public double FractionOf(int value) =>
        Math.Clamp((double)(value - Minimum) / (Maximum - Minimum), 0d, 1d);

    /// <summary>The angle a value sits at, in degrees clockwise from three o'clock.</summary>
    public double AngleOf(int value) => StartDegrees + (SweepDegrees * FractionOf(value));

    /// <summary>
    /// The value a point on the track means. Rounds, so dragging lands on whole
    /// units rather than leaving the reading a fraction short of what it looks like.
    /// </summary>
    public int ValueAtAngle(double degrees)
    {
        var fraction = Math.Clamp((degrees - StartDegrees) / SweepDegrees, 0d, 1d);
        return (int)Math.Round(Minimum + (fraction * (Maximum - Minimum)), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The value a point means, given as an offset from the dial's centre in the
    /// view's own coordinates (y grows downward).
    ///
    /// The open quarter at the bottom is not part of the track, so a finger that
    /// wanders into it takes the end it is nearest — jumping to the far end of the
    /// dial because a thumb slipped below it is the one thing a tempo control must
    /// not do.
    /// </summary>
    public int ValueAtPoint(double dx, double dy)
    {
        if (dx == 0d && dy == 0d)
        {
            return Minimum; // the pivot has no direction to read
        }

        var degrees = (Math.Atan2(dy, dx) * 180d / Math.PI + 360d) % 360d;

        // The sweep runs 135° → 405°, so the part of it past the wrap (0°–45°) belongs
        // at the top of that range rather than at the bottom of the circle.
        var end = StartDegrees + SweepDegrees;
        var wrapped = end - 360d;
        if (degrees <= wrapped)
        {
            degrees += 360d;
        }

        // What is left below the start is the open quarter at the bottom. Take the end
        // it is nearest: jumping to the far end of the dial because a thumb slipped
        // below it is the one thing a tempo control must not do.
        if (degrees < StartDegrees)
        {
            degrees = degrees - wrapped < StartDegrees - degrees ? end : StartDegrees;
        }

        return ValueAtAngle(degrees);
    }

    /// <summary>
    /// Where the knob's centre goes for a value. Screen coordinates, so y grows
    /// downward and the sweep runs clockwise from the lower left.
    /// </summary>
    public (double X, double Y) KnobAt(int value, double centreX, double centreY, double radius)
    {
        var radians = AngleOf(value) * Math.PI / 180d;
        return (centreX + (radius * Math.Cos(radians)), centreY + (radius * Math.Sin(radians)));
    }
}
