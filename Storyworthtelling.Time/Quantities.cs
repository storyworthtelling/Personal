using System.Globalization;

namespace Storyworthtelling.Time;

/// <summary>
/// The suite's one way to say how many, how big and how long (audit W11.5, DESIGN.md §14). Every number is written in
/// the invariant culture, so a phone set to German prints "80,000 words", not "80.000"; a count agrees with its noun
/// rather than printing "1 tracks" or "(s)"; a size says KB, MB or GB; a duration is "42 min".
/// </summary>
public static class Quantities
{
    /// <summary>A whole number grouped by thousands: "12,500".</summary>
    public static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>"1 track", "3 tracks"; <paramref name="many"/> for a noun that does not take an s.</summary>
    public static string Count(long count, string one, string? many = null) =>
        $"{Number(count)} {(count == 1 ? one : many ?? one + "s")}";

    /// <summary>"once", "twice", "5 times".</summary>
    public static string Times(long count) => count switch
    {
        1 => "once",
        2 => "twice",
        _ => $"{Number(count)} times",
    };

    /// <summary>"900 bytes", "2 KB", "3.4 MB", "5.0 GB".</summary>
    public static string Size(long bytes)
    {
        const double Kilo = 1024;
        if (bytes < Kilo)
            return Count(bytes, "byte");
        if (bytes < Kilo * Kilo)
            return Math.Round(bytes / Kilo, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " KB";
        if (bytes < Kilo * Kilo * Kilo)
            return (bytes / (Kilo * Kilo)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        return (bytes / (Kilo * Kilo * Kilo)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>"42 min", "1 hr", "1 hr 20 min".</summary>
    public static string Minutes(long minutes)
    {
        if (minutes < 60)
            return $"{Number(minutes)} min";
        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{Number(hours)} hr" : $"{Number(hours)} hr {rest} min";
    }

    /// <summary>A span said to the nearest minute.</summary>
    public static string Duration(TimeSpan span) => Minutes((long)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero));

    /// <summary>"60 Hz", "1 kHz", "3.6 kHz".</summary>
    public static string Frequency(int hertz) =>
        hertz < 1000
            ? $"{hertz} Hz"
            : (hertz / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz";
}
