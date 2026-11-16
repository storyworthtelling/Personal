using Folio.Domain.Analysis;

namespace Folio.Domain.Reading;

/// <summary>
/// What every whole-book pass of a draft is told besides the manuscript: the genre the consolidation read
/// from the text, and, for a book in a series, the earlier book's canon (R27). Both are the same for every
/// pass of the draft. The canon is cached with the manuscript; the genre sits in each pass's instruction.
/// Neither is another reader's notes, so the readers still read alone.
/// </summary>
public sealed record ReadingFrame(GenreFrame Genre, string SeriesCanon)
{
    public static implicit operator ReadingFrame(GenreFrame genre) => new(genre, string.Empty);

    public bool HasSeriesCanon => !string.IsNullOrWhiteSpace(SeriesCanon);

    /// <summary>The earlier book's canon as the readers are given it: cast, settings, where the story stood, open threads.</summary>
    public static string CanonText(SeriesCanon canon)
    {
        if (string.IsNullOrWhiteSpace(canon.PriorBookTitle))
            return string.Empty;

        var lines = new List<string> { $"SERIES BIBLE: the canon of the earlier book in this series, \"{canon.PriorBookTitle}\"." };
        if (canon.Cast.Count > 0)
            lines.Add("CHARACTERS:\n" + string.Join("\n", canon.Cast.Select(entity => $"- {entity.Name}: {entity.Description}")));
        if (canon.Settings.Count > 0)
            lines.Add("SETTINGS:\n" + string.Join("\n", canon.Settings.Select(entity => $"- {entity.Name}: {entity.Description}")));
        if (!string.IsNullOrWhiteSpace(canon.PriorContinuitySummary))
            lines.Add("WHERE THE STORY STOOD AT THE END OF THAT BOOK:\n" + canon.PriorContinuitySummary);
        if (canon.OpenThreads.Count > 0)
            lines.Add("THREADS IT LEFT OPEN:\n" + string.Join("\n", canon.OpenThreads.Select(thread => $"- {thread.Detail}")));
        return string.Join("\n\n", lines);
    }
}
