using Folio.Domain.Shelf;

namespace Folio.Tests.Shelf;

/// <summary>
/// The three lines beside a book's cover on its own page. They are here rather than in the screen
/// because "three drafts" and "Book 1 of three" are the same small decision the shelf already makes
/// about spelling small numbers, and two copies of that decision drift.
/// </summary>
public class BookLinesTests
{
    [Fact]
    public void ABookInASeriesSaysWhereItStands() =>
        Assert.Equal("Book 1 of three", BookLines.Standing(inSeries: true, 1, seriesLength: 3, "Completed"));

    /// <summary>
    /// A series whose later books do not exist yet is still a series, and the book is still its
    /// first. Saying "Book 1 of one" would be a sentence nobody writes.
    /// </summary>
    [Fact]
    public void TheFirstBookOfAnUnfinishedSeriesIsJustBookOne() =>
        Assert.Equal("Book 1", BookLines.Standing(inSeries: true, 1, seriesLength: 1, "Completed"));

    [Fact]
    public void AStandaloneBookSaysWhatStateItIsIn()
    {
        Assert.Equal("Reviewed", BookLines.Standing(false, 1, 0, "Completed"));
        Assert.Equal("Being read", BookLines.Standing(false, 1, 0, "ComposingAnalysis"));
        Assert.Equal("Stopped", BookLines.Standing(false, 1, 0, "Failed"));
        Assert.Equal("Stopped", BookLines.Standing(false, 1, 0, "Cancelled"));
    }

    [Fact]
    public void DraftsAreCountedInWordsAndDatedByTheLatest() =>
        Assert.Equal("Three drafts · latest 12 Sep",
            BookLines.Drafts(3, new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void OneDraftIsNotOneDrafts() =>
        Assert.Equal("One draft · latest 2 Jun",
            BookLines.Drafts(1, new DateTimeOffset(2026, 6, 2, 8, 0, 0, TimeSpan.Zero)));

    /// <summary>
    /// Spelled to ten, because the report's own pages say "Reader 3 of seven" — and a document that
    /// spells three and prints 7 in one sentence reads as two documents.
    /// </summary>
    [Fact]
    public void SevenIsSpelled() =>
        Assert.Equal("seven", BookLines.Spell(7));

    /// <summary>
    /// Past the words the frame spells, figures. A book on its eleventh draft is not a book the
    /// sentence "eleven drafts" flatters, and the numeral is what the author is counting in by then.
    /// </summary>
    [Fact]
    public void PastTenItIsFigures() =>
        Assert.StartsWith("11 drafts", BookLines.Drafts(11, DateTimeOffset.UtcNow));

    [Fact]
    public void NoDraftsIsNoLine() =>
        Assert.Equal(string.Empty, BookLines.Drafts(0, null));
}
