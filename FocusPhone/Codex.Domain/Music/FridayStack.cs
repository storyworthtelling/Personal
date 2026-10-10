namespace Codex.Domain.Music;

/// <summary>
/// How many records are in the crate on a Friday, counted separately for music and for
/// podcasts.
///
/// It is a setting rather than a constant because how many new things fit in a week
/// is a fact about the owner, not about the app, and music and podcasts are not the same
/// appetite. It is clamped because nothing carries over — an unfinished stack is replaced,
/// not stacked on — so a count nobody could get through in a week does not build a
/// backlog, it just wastes most of the run.
///
/// Zero is a real answer for either kind: a week with no podcasts in it is a choice,
/// not a mistake to correct. The crate as a whole is simply empty when both are zero.
/// </summary>
public static class FridayStack
{
    /// <summary>The lowest count for either kind. Zero switches that kind off for the week.</summary>
    public const int Smallest = 0;

    /// <summary>Above this, per kind, the week ends long before the stack does.</summary>
    public const int Largest = 40;

    /// <summary>A fortnight's records for a week, split by the usual appetite for each.</summary>
    public const int DefaultMusic = 10;

    /// <summary>The podcast share of the default fortnight.</summary>
    public const int DefaultPodcasts = 4;

    public static int Clamp(int count) => Math.Clamp(count, Smallest, Largest);
}
