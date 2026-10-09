using Pulp.Domain.Ratings;

namespace Pulp.Domain.Children;

/// <summary>
/// A child's rating ceiling, as an age band (D127) — one choice the parent makes, read against both
/// MPA film and US TV ratings. Also the voice's reading level for a child (D233); no birthday is
/// stored.
/// </summary>
public enum AgeBand
{
    UnderSeven,
    SevenToNine,
    TenToTwelve,
    ThirteenToSixteen,
    Seventeen,
}

/// <summary>
/// Which ratings each band admits, per rating rather than per chip step: TV-G is "suitable for all
/// ages" though the chip calls it family, and NC-17 is "no one 17 and under" though the chip calls R
/// mature too. An unrated title is admitted by no band (D128 — only an allow-list shows it).
/// </summary>
public static class AgeBands
{
    // The youngest band that may see each rating. A rating absent here (NC-17, X) no child band admits.
    private static readonly Dictionary<string, AgeBand> YoungestAdmitted = new(StringComparer.Ordinal)
    {
        ["G"] = AgeBand.UnderSeven,
        ["TV-Y"] = AgeBand.UnderSeven,
        ["TV-G"] = AgeBand.UnderSeven,
        ["TV-Y7"] = AgeBand.SevenToNine,
        ["TV-Y7-FV"] = AgeBand.SevenToNine,
        ["PG"] = AgeBand.TenToTwelve,
        ["TV-PG"] = AgeBand.TenToTwelve,
        ["PG-13"] = AgeBand.ThirteenToSixteen,
        ["TV-14"] = AgeBand.ThirteenToSixteen,
        ["R"] = AgeBand.Seventeen,
        ["TV-MA"] = AgeBand.Seventeen,
    };

    public static bool Allows(AgeBand ceiling, ContentRating rating)
    {
        ArgumentNullException.ThrowIfNull(rating);
        return rating.IsRated
            && YoungestAdmitted.TryGetValue(rating.Code, out var youngest)
            && ceiling >= youngest;
    }

    public static int Youngest(AgeBand band) => band switch
    {
        AgeBand.UnderSeven => 0,
        AgeBand.SevenToNine => 7,
        AgeBand.TenToTwelve => 10,
        AgeBand.ThirteenToSixteen => 13,
        AgeBand.Seventeen => 17,
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, "An age band with no ages."),
    };

    public static int Oldest(AgeBand band) => band switch
    {
        AgeBand.UnderSeven => 6,
        AgeBand.SevenToNine => 9,
        AgeBand.TenToTwelve => 12,
        AgeBand.ThirteenToSixteen => 16,
        AgeBand.Seventeen => 17,
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, "An age band with no ages."),
    };
}
