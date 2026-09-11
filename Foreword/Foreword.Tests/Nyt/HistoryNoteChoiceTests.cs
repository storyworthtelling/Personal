using Foreword.Domain.Brief;
using Xunit;

namespace Foreword.Tests.Nyt;

/// <summary>
/// Which of the day's eighty-odd historical events reaches the page.
///
/// <para>Wikipedia returns everything, including last year. "On this day in 2025" is not history —
/// it is the news, and the brief has eight pages of that already. What earns a line is old enough
/// to have been forgotten and short enough to read in passing.</para>
/// </summary>
public class HistoryNoteChoiceTests
{
    private static readonly DateOnly Today = new(2026, 9, 11);

    private static HistoryNote Note(int year, string text) => new(year, text);

    [Fact]
    public void SomethingRecentIsNotHistory()
    {
        var chosen = HistoryNoteChoice.Pick(
        [
            Note(2025, "A resupply mission is launched to the International Space Station."),
            Note(1297, "The Battle of Stirling Bridge is fought."),
        ], Today);

        Assert.Equal(1297, chosen!.Year);
    }

    /// <summary>
    /// The oldest, which is the one most likely to be new to the reader. A day's feed runs from
    /// antiquity to last year and the interesting end is not the recent one.
    /// </summary>
    [Fact]
    public void TheOldestQualifyingNoteWins()
    {
        var chosen = HistoryNoteChoice.Pick(
        [
            Note(1973, "A coup in Chile."),
            Note(1297, "The Battle of Stirling Bridge is fought."),
            Note(1857, "The Mountain Meadows massacre."),
        ], Today);

        Assert.Equal(1297, chosen!.Year);
    }

    /// <summary>
    /// A very long entry is passed over rather than truncated. A factoid cut off mid-clause is worse
    /// than a different factoid, and there are eighty to choose from.
    /// </summary>
    [Fact]
    public void AnOverlongNoteIsPassedOverRatherThanCut()
    {
        var chosen = HistoryNoteChoice.Pick(
        [
            Note(1297, new string('x', HistoryNoteChoice.MaxLength + 1)),
            Note(1857, "The Mountain Meadows massacre."),
        ], Today);

        Assert.Equal(1857, chosen!.Year);
    }

    /// <summary>
    /// Nothing qualifying means nothing printed. A factoid is the most skippable thing in the
    /// document; an empty heading over a blank line is worse than no heading.
    /// </summary>
    [Fact]
    public void NothingQualifyingPrintsNothing()
    {
        Assert.Null(HistoryNoteChoice.Pick([Note(2025, "Something last year.")], Today));
        Assert.Null(HistoryNoteChoice.Pick([], Today));
    }

    /// <summary>
    /// A note with no text is not a note. The feed is user-edited, so an entry can be an empty
    /// string with a year attached, and printing the year alone would read as a typesetting fault.
    /// </summary>
    [Fact]
    public void AnEmptyNoteIsNotChosen()
    {
        Assert.Null(HistoryNoteChoice.Pick([Note(1297, "   ")], Today));
    }

    /// <summary>
    /// The cut-off moves with the date rather than being a fixed year, so the rule does not quietly
    /// change meaning as time passes — in 2050 "not last year" still means not last year.
    /// </summary>
    [Fact]
    public void TheCutOffIsRelativeToTheDay()
    {
        var recent = Note(Today.Year - HistoryNoteChoice.MinimumAge + 1, "Too recent.");
        var old = Note(Today.Year - HistoryNoteChoice.MinimumAge, "Old enough.");

        Assert.Null(HistoryNoteChoice.Pick([recent], Today));
        Assert.Equal(old.Year, HistoryNoteChoice.Pick([old], Today)!.Year);
    }
}
