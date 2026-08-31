using Codex.Domain.Design;

namespace Codex.Domain.Music;

/// <summary>
/// Folds a week's vetoes into the Taste Profile.
///
/// This is the one <b>deliberate exception</b> to Score's passive-only learning: a
/// veto is the owner saying something outright, which is exactly what ratings are.
/// What keeps it from becoming a ratings system is that it is batched — the strikes
/// go in once, when the crate is replaced, rather than the moment they are made. A
/// tap that changes the profile immediately is a rating; a week's worth of margin
/// notes read together is an observation.
///
/// Two rules keep the profile the profile. The recorded vetoes are capped, so a
/// year of Fridays cannot bury what Claude actually learned; and trimming only ever
/// drops veto lines — the profile's own avoids are not ours to age out. That is why
/// each line is written in a recognizable form rather than as free prose.
/// </summary>
public static class VetoLedger
{
    /// <summary>How many struck records the profile carries at once.</summary>
    public const int MaxRecorded = 20;

    private const string Prefix = "passed on ";

    public static TasteProfile Fold(TasteProfile profile, IReadOnlyList<CratePick> vetoed)
    {
        if (vetoed.Count == 0)
        {
            return profile;
        }

        var theirs = profile.Avoids.Where(line => !IsRecorded(line)).ToList();
        var recorded = profile.Avoids.Where(IsRecorded).ToList();

        foreach (var line in vetoed.Select(Describe))
        {
            if (!recorded.Any(existing => FilterMatch.Equals(existing, line)))
            {
                recorded.Add(line);
            }
        }

        // Oldest first, so trimming from the front keeps the most recent week.
        if (recorded.Count > MaxRecorded)
        {
            recorded.RemoveRange(0, recorded.Count - MaxRecorded);
        }

        return profile with { Avoids = [.. theirs, .. recorded] };
    }

    /// <summary>How a struck record reads in the profile, and how it is recognized
    /// again later.</summary>
    public static string Describe(CratePick pick) => $"{Prefix}{pick.Title} by {pick.Subtitle}";

    private static bool IsRecorded(string line) => line.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
}
