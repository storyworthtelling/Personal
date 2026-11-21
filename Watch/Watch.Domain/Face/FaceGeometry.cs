namespace Watch.Domain.Face;

/// <summary>
/// The face's shapes as fractions of the face's radius R (WD §10; A46; the approved study 1b in
/// <c>face-studies-r2.html</c> and <c>screens-r1.html</c>'s <c>face</c>). FaceStudio draws the Watch Face
/// Format from these; nothing else places anything on the face.
/// </summary>
public static class FaceGeometry
{
    /// <summary>The hour shape before noon: about half the face (WD §10).</summary>
    public const double MorningRadius = 0.5;

    /// <summary>The hour shape after noon, drawn smaller so its ring can hold it (study 1b).</summary>
    public const double AfternoonRadius = 0.42;

    /// <summary>One o'clock's dot, the same size morning and afternoon.</summary>
    public const double DotRadius = 0.1;

    /// <summary>The after-noon ring (WD §10).</summary>
    public const double RingRadius = 0.62;

    /// <summary>The thin bright rim round a lit shape (WD §3: 1.8% of the face's radius).</summary>
    public const double RimStroke = 0.018;

    /// <summary>Two o'clock's line, which has no inside to fill.</summary>
    public const double LineStroke = 0.07;

    /// <summary>The after-noon ring: thinner and quieter than the rim.</summary>
    public const double RingStroke = 0.012;

    /// <summary>The always-on outline of a shape (WD §2).</summary>
    public const double AlwaysOnStroke = 0.022;

    /// <summary>Two o'clock's line in always-on.</summary>
    public const double AlwaysOnLineStroke = 0.04;

    /// <summary>The date's centre, below the face's centre.</summary>
    public const double DateDrop = 0.76;

    /// <summary>The glass's highlight, high on the shape's left, as a fraction of the shape's radius (WD §2).</summary>
    public const double HighlightX = -0.35;

    public const double HighlightY = -0.45;

    /// <summary>How far the glass's light reaches from the highlight, as a fraction of the shape's radius.</summary>
    public const double GlassReach = 1.45;

    /// <summary>The hour shape's radius: the morning size, or the smaller afternoon size inside its ring.</summary>
    public static double ShapeRadius(HourShape shape) => shape.Afternoon ? AfternoonRadius : MorningRadius;

    /// <summary>
    /// How much of the round face the always-on outline lights (WD §2: under 5%): each stroke's length
    /// times its width, over the face's area. Round ends and overlaps at corners are not counted.
    /// </summary>
    public static double AlwaysOnLit(HourShape shape)
    {
        double lit;
        if (shape.Sides == 1)
        {
            lit = 2 * Math.PI * DotRadius * AlwaysOnStroke;
        }
        else if (shape.Sides == 2)
        {
            lit = 2 * ShapeRadius(shape) * AlwaysOnLineStroke;
        }
        else
        {
            lit = Perimeter(shape.Sides, ShapeRadius(shape)) * AlwaysOnStroke;
        }

        if (shape.Afternoon)
        {
            var ring = shape.Sides <= 2 ? 2 * Math.PI * RingRadius : Perimeter(shape.Sides, RingRadius);
            lit += ring * RingStroke;
        }

        return lit / Math.PI;
    }

    private static double Perimeter(int sides, double radius) => 2 * sides * radius * Math.Sin(Math.PI / sides);
}
