namespace Pulp.Domain.Ratings;

/// <summary>Which rating system a title's rating comes from. <see cref="None"/> is unrated.</summary>
public enum RatingSystem
{
    None,
    Mpa,
    UsTv,
}

/// <summary>
/// The four steps of the rating chip (D215): kids · family · teen · mature. Deliberately not ordered
/// for comparison against a child's ceiling — that is <c>AgeBands</c>' job, per rating — so an
/// unrated title can never slip under a ceiling by sorting below it.
/// </summary>
public enum RatingStep
{
    Kids,
    Family,
    Teen,
    Mature,
}

/// <summary>
/// A title's rating as the rating source gave it (OMDb's <c>Rated</c>), read into one of the two
/// systems Pulp knows — MPA film and US TV — or unrated. Anything outside those two (historic
/// "Approved", "Passed", foreign or invented codes, "N/A") is unrated rather than guessed at: an
/// unrated title is hidden from a child (D128), which is the safe direction for a code nobody mapped.
/// </summary>
public sealed record ContentRating
{
    private static readonly Dictionary<string, (RatingSystem System, RatingStep Step)> Known =
        new(StringComparer.Ordinal)
        {
            ["G"] = (RatingSystem.Mpa, RatingStep.Kids),
            ["PG"] = (RatingSystem.Mpa, RatingStep.Family),
            ["PG-13"] = (RatingSystem.Mpa, RatingStep.Teen),
            ["R"] = (RatingSystem.Mpa, RatingStep.Mature),
            ["NC-17"] = (RatingSystem.Mpa, RatingStep.Mature),

            // X preceded NC-17; it is adults-only, so it reads as mature rather than unrated.
            ["X"] = (RatingSystem.Mpa, RatingStep.Mature),
            ["TV-Y"] = (RatingSystem.UsTv, RatingStep.Kids),
            ["TV-Y7"] = (RatingSystem.UsTv, RatingStep.Kids),
            ["TV-Y7-FV"] = (RatingSystem.UsTv, RatingStep.Kids),
            ["TV-G"] = (RatingSystem.UsTv, RatingStep.Family),
            ["TV-PG"] = (RatingSystem.UsTv, RatingStep.Family),
            ["TV-14"] = (RatingSystem.UsTv, RatingStep.Teen),
            ["TV-MA"] = (RatingSystem.UsTv, RatingStep.Mature),
        };

    private ContentRating(RatingSystem system, string code, RatingStep? step)
    {
        System = system;
        Code = code;
        Step = step;
    }

    /// <summary>No rating either system recognises.</summary>
    public static ContentRating Unrated { get; } = new(RatingSystem.None, string.Empty, null);

    public RatingSystem System { get; }

    /// <summary>The canonical code ("PG-13", "TV-14"); empty when unrated.</summary>
    public string Code { get; }

    /// <summary>The chip step; null when unrated.</summary>
    public RatingStep? Step { get; }

    public bool IsRated => Step is not null;

    /// <summary>Reads a raw rating whatever its casing, spacing or dash; unknown is unrated.</summary>
    public static ContentRating Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Unrated;
        }

        var code = Normalise(raw);
        return Known.TryGetValue(code, out var known) ? new ContentRating(known.System, code, known.Step) : Unrated;
    }

    private static string Normalise(string raw)
    {
        var parts = raw.Trim().ToUpperInvariant()
            .Replace('–', '-')
            .Replace('—', '-')
            .Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join('-', parts);
    }
}
