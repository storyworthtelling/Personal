namespace Watch.Domain.Design;

/// <summary>
/// How well one colour reads on another (WD §12: words keep 4.5:1), by the WCAG ratio of their relative luminances as
/// the screen draws them (<see cref="Srgb"/>). A pill's words take whichever of the near-black and the ink reads better
/// on the pill's glass at that moment, since an app's glass drifts across the day (WD §4.2).
/// </summary>
public static class WatchContrast
{
    public static double Ratio(Oklch first, Oklch second)
    {
        var one = Luminance(first);
        var two = Luminance(second);
        return (Math.Max(one, two) + 0.05) / (Math.Min(one, two) + 0.05);
    }

    /// <summary>The words for a ground: the near-black on light glass, the ink on dark.</summary>
    public static Oklch WordsOn(Oklch ground) =>
        Ratio(WatchTokens.Neutral.OnGlass, ground) >= Ratio(WatchTokens.Neutral.Ink, ground) ? WatchTokens.Neutral.OnGlass : WatchTokens.Neutral.Ink;

    private static double Luminance(Oklch colour)
    {
        var argb = Srgb.Argb(colour);
        return 0.2126 * Linear((argb >> 16) & 0xFF) + 0.7152 * Linear((argb >> 8) & 0xFF) + 0.0722 * Linear(argb & 0xFF);
    }

    private static double Linear(int channel)
    {
        var unit = channel / 255.0;
        return unit <= 0.04045 ? unit / 12.92 : Math.Pow((unit + 0.055) / 1.055, 2.4);
    }
}
