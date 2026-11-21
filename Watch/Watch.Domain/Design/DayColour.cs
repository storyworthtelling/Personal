namespace Watch.Domain.Design;

/// <summary>
/// An app's colour at a time of day (WD §4.2, §13; P14 W-1.1.2): the night end at midnight, the day
/// end at noon, easing between on the sun's rhythm. The only way a watch screen gets an app's colour.
/// </summary>
public static class DayColour
{
    private const double FullTurn = 360;

    public static Oklch At(WatchApp app, TimeOnly time)
    {
        var range = WatchTokens.RangeOf(app);
        var position = Position(time);
        return new Oklch(
            Mix(range.Night.L, range.Day.L, position),
            Mix(range.Night.C, range.Day.C, position),
            MixHue(range.Night.H, range.Day.H, position));
    }

    /// <summary>0 at midnight, 1 at noon, (1 − cos(2π·hours/24)) / 2 between, so it never jumps.</summary>
    private static double Position(TimeOnly time)
    {
        var hours = time.ToTimeSpan().TotalHours;
        return (1 - Math.Cos(2 * Math.PI * hours / 24)) / 2;
    }

    private static double Mix(double from, double to, double position) => from + (to - from) * position;

    /// <summary>Along the shorter way round the wheel, so Dash passes through plum rather than green.</summary>
    private static double MixHue(double from, double to, double position)
    {
        var difference = Normalise(to - from);
        if (difference > FullTurn / 2)
        {
            difference -= FullTurn;
        }
        return Normalise(from + difference * position);
    }

    private static double Normalise(double hue)
    {
        var wrapped = hue % FullTurn;
        return wrapped < 0 ? wrapped + FullTurn : wrapped;
    }
}
