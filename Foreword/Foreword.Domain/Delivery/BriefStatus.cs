using Storyworthtelling.Time;

namespace Foreword.Domain.Delivery;

/// <summary>Why a morning's brief went out as the crossword alone.</summary>
public enum BriefShortfall
{
    NewsUnreachable,
    NoStories,
    NoStoryFetched,
    LayoutFailed,
}

/// <summary>
/// What the delivery line says about the brief (audit W11): "6 stories and the crossword", or "the crossword only" and
/// why, in plain words. The detail of a failure goes to the delivery log, never onto this line.
/// </summary>
public static class BriefStatus
{
    public static string Composed(int stories, int fromSummary) =>
        Quantities.Count(stories, "story", "stories") + " and the crossword"
        + (fromSummary > 0 ? $", {Quantities.Number(fromSummary)} from their summary only" : string.Empty);

    public static string CrosswordOnly(BriefShortfall why) => "the crossword only — " + why switch
    {
        BriefShortfall.NewsUnreachable => "the news couldn’t be fetched",
        BriefShortfall.NoStories => "the news had no stories",
        BriefShortfall.NoStoryFetched => "no story could be fetched",
        _ => "the stories couldn’t be laid out",
    };
}
