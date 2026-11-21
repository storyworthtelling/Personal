namespace Watch.Domain.Face;

/// <summary>A point in the face's frame: x to the right, y down, the centre at (0, 0).</summary>
public readonly record struct ShapePoint(double X, double Y);

/// <summary>
/// The face's hour (WD §3, §10, §13; A46; P14 W-1.1.3): as many sides as the hour on a 12-hour clock
/// (1 a dot, 2 a line, 3 a triangle … 12), standing on a point at twelve o'clock; after noon the second
/// ring; and a hue from the minute going round the full wheel once an hour (WD §4.1).
/// </summary>
public sealed record HourShape(int Sides, bool Afternoon, double Hue)
{
    private const double FullTurn = 360;

    public static HourShape For(TimeOnly time)
    {
        var twelveHour = time.Hour % 12;
        var sides = twelveHour == 0 ? 12 : twelveHour;
        var minutes = time.Minute + time.Second / 60.0;
        return new HourShape(sides, time.Hour >= 12, minutes / 60 * FullTurn);
    }

    /// <summary>
    /// The shape's points on a circle of <paramref name="radius"/>, the first straight up at twelve and
    /// the rest clockwise. A dot is the single centre point; a line is its two ends, top then bottom.
    /// </summary>
    public IReadOnlyList<ShapePoint> Vertices(double radius)
    {
        if (Sides == 1)
        {
            return [new ShapePoint(0, 0)];
        }

        var points = new ShapePoint[Sides];
        for (var index = 0; index < Sides; index++)
        {
            var angle = 2 * Math.PI * index / Sides;
            points[index] = new ShapePoint(radius * Math.Sin(angle), -radius * Math.Cos(angle));
        }
        return points;
    }
}
