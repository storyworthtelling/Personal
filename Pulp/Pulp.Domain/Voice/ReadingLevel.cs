using Pulp.Domain.Children;

namespace Pulp.Domain.Voice;

/// <summary>
/// The voice's reading level for a child, from their rating-ceiling age band (D233) — no birthday is
/// stored — and the moderation D232 asks of every child's line.
/// </summary>
public static class ReadingLevel
{
    public static string Guidance(AgeBand band)
    {
        var youngest = AgeBands.Youngest(band);
        var oldest = AgeBands.Oldest(band);
        var ages = youngest == 0
            ? $"under {NumberWords.Of(oldest + 1)}"
            : youngest == oldest ? NumberWords.Of(youngest) : $"{NumberWords.Of(youngest)} to {NumberWords.Of(oldest)}";
        return $"Write for a child aged {ages}: short words, one idea, nothing frightening.";
    }
}
