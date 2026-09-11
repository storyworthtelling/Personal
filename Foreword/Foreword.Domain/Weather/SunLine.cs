using System.Globalization;

namespace Foreword.Domain.Weather;

/// <summary>
/// The foot of the weather page. Daylight is on it because a smart task can require daylight, so
/// the number has a job beyond being interesting.
/// </summary>
public static class SunLine
{
    public static string Daylight(SunAndMoon sun)
    {
        if (sun.Sunrise is not { } rise || sun.Sunset is not { } set || set <= rise)
        {
            return string.Empty;
        }

        var span = set - rise;
        return $"{(int)span.TotalHours}h {span.Minutes}m of daylight";
    }

    /// <summary>
    /// WANING_GIBBOUS becomes "Waning gibbous". Left alone it would print a shouted enum onto a
    /// sheet of paper — the sort of thing that survives to a printed page precisely because it is
    /// legible enough that nobody calls it a bug.
    ///
    /// <para>An unspecified or unrecognised phase is left out rather than guessed at. There is no
    /// sensible fallback for the moon.</para>
    /// </summary>
    public static string Moon(string phase)
    {
        if (string.IsNullOrWhiteSpace(phase) || phase.Contains("UNSPECIFIED", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var words = phase.Trim().Replace('_', ' ').ToLowerInvariant();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words[..1]) + words[1..];
    }
}
