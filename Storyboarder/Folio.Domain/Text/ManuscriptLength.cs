namespace Folio.Domain.Text;

/// <summary>What a manuscript's own word count says it is, independent of genre.</summary>
public enum StoryLength
{
    ShortStory,
    Novella,
    Novel,
}

/// <summary>
/// Classifies a manuscript by length so a prompt can call it what it is rather than assuming "novel"
/// for everything the app is handed — a one-chapter, 1,200-word short story judged by novel-scale
/// questions (chapter-by-chapter pacing, a theme hierarchy) has nothing to answer and invents one.
///
/// <para>The thresholds follow the boundaries publishing/genre organizations commonly draw (SFWA's
/// award categories among them): under 10,000 words reads as a short story, 10,000–39,999 as a
/// novella, 40,000 and up as a novel. Deliberately three bands, not the finer novelette/novelette
/// splits some organizations use — a prompt only needs a noun to call the manuscript by.</para>
/// </summary>
public static class ManuscriptLength
{
    private const int NovellaFloor = 10_000;
    private const int NovelFloor = 40_000;

    public static StoryLength Classify(int wordCount) =>
        wordCount >= NovelFloor ? StoryLength.Novel
        : wordCount >= NovellaFloor ? StoryLength.Novella
        : StoryLength.ShortStory;

    /// <summary>Counts the manuscript's own words rather than trusting a caller's count.</summary>
    public static StoryLength Classify(string manuscript) =>
        Classify(string.IsNullOrWhiteSpace(manuscript)
            ? 0
            : manuscript.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);

    /// <summary>The noun a prompt should call this manuscript by.</summary>
    public static string Noun(StoryLength length) => length switch
    {
        StoryLength.ShortStory => "short story",
        StoryLength.Novella => "novella",
        StoryLength.Novel => "novel",
        _ => throw new ArgumentOutOfRangeException(nameof(length), length, "Unhandled story length."),
    };
}
