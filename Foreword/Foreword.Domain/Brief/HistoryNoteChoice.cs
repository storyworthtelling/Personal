namespace Foreword.Domain.Brief;

/// <summary>
/// Which of the day's eighty-odd historical events reaches the page.
///
/// <para>Wikipedia returns everything, last year included, and "on this day in 2025" is not history
/// — it is the news, of which the brief already has eight pages. What earns a line is old enough to
/// have been forgotten and short enough to read in passing.</para>
/// </summary>
public static class HistoryNoteChoice
{
    /// <summary>
    /// How old a thing has to be to count as history. Relative to the day rather than a fixed year,
    /// so the rule does not quietly change meaning as time passes.
    /// </summary>
    public const int MinimumAge = 50;

    /// <summary>
    /// Longer than this and it is passed over rather than cut. A factoid truncated mid-clause is
    /// worse than a different factoid, and there are eighty to choose from.
    /// </summary>
    public const int MaxLength = 180;

    /// <summary>
    /// The oldest note that qualifies, or null. The oldest because it is the one most likely to be
    /// new to the reader — a day's feed runs from antiquity to last year, and the interesting end is
    /// not the recent one.
    /// </summary>
    public static HistoryNote? Pick(IReadOnlyList<HistoryNote> notes, DateOnly today) =>
        notes
            .Where(note => !string.IsNullOrWhiteSpace(note.Text))
            .Where(note => note.Text.Trim().Length <= MaxLength)
            .Where(note => today.Year - note.Year >= MinimumAge)
            .OrderBy(note => note.Year)
            .FirstOrDefault();
}
