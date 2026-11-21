using System.Globalization;
using Watch.Domain.Design;
using Watch.Domain.Face;

namespace FaceStudio;

/// <summary>
/// The face's colours as the format writes them. Every one is converted at run time from Watch.Domain
/// (<see cref="Srgb"/> over <see cref="FaceColour"/> and <see cref="WatchTokens"/>), so no colour is
/// written in this tool's source (WD §13).
/// </summary>
public static class FaceColours
{
    /// <summary>A colour attribute: <c>#AARRGGBB</c>.</summary>
    public static string Hex(Oklch colour, double alpha = 1) =>
        "#" + Srgb.Argb(colour, alpha).ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>A tone's colour at the top of the hour, the attribute's value before any expression runs.</summary>
    public static string AtTheHour(Func<double, Oklch> tone) => Hex(tone(0));

    /// <summary>
    /// The colour a tone takes as the minute goes round the wheel (WD §4.1): one opaque <c>RRGGBB</c>
    /// for each minute, from <see cref="HourShape.For"/>'s hue, chosen by <c>[MINUTE]</c>. The format's
    /// <c>extractColorFromColors</c> (version 4) takes the list as a quoted string.
    /// </summary>
    public static string ByMinute(Func<double, Oklch> tone)
    {
        var list = string.Join(' ', Enumerable.Range(0, 60).Select(minute =>
            (Srgb.Argb(tone(HourShape.For(new TimeOnly(0, minute)).Hue)) & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture)));

        // Sixty colours, one a minute: with interpolation on, value m/59 lands on colour m whether the
        // format reads the value as a position along the list or as a share of it. The divisor is a
        // fraction, so the share is never read as whole-number division, which would choose the first
        // colour for every minute but the last (release A fix round).
        return $"extractColorFromColors(\"{list}\", true, [MINUTE] / 59.0)";
    }
}
