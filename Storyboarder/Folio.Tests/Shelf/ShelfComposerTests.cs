using Folio.Domain.Shelf;
using Xunit;

namespace Folio.Tests.Shelf;

/// <summary>
/// How books are dealt onto shelves. The rule comes from the approved frame: <b>a series always gets
/// its own shelf</b>, never mixed with standalone books, so its bookends bracket a whole row rather
/// than a run inside one — and a long series simply takes two shelves.
/// </summary>
public class ShelfComposerTests
{
    private static ShelfBook Book(string title, string? series = null, int sequence = 0, int words = 90_000) =>
        new(Guid.NewGuid(), title, series, sequence, words);

    [Fact]
    public void ASeriesGetsAShelfToItself()
    {
        var rows = ShelfComposer.Compose(
        [
            Book("A Quiet Inventory"),
            Book("The Salt Line", "The Salt Line", 1),
            Book("Northing"),
            Book("Saltmarsh", "The Salt Line", 2),
        ]);

        var series = Assert.Single(rows, row => row.SeriesName == "The Salt Line");
        Assert.Equal(["The Salt Line", "Saltmarsh"], series.Books.Select(book => book.Title));
        Assert.DoesNotContain(series.Books, book => book.Series is null);
    }

    [Fact]
    public void ASeriesIsOrderedBySequenceNotByArrival()
    {
        var rows = ShelfComposer.Compose(
        [
            Book("The Turning Tide", "The Salt Line", 3),
            Book("The Salt Line", "The Salt Line", 1),
            Book("Saltmarsh", "The Salt Line", 2),
        ]);

        Assert.Equal(
            ["The Salt Line", "Saltmarsh", "The Turning Tide"],
            Assert.Single(rows).Books.Select(book => book.Title));
    }

    // "A long series simply takes two shelves" — it wraps rather than shrinking its spines.
    [Fact]
    public void ALongSeriesWrapsOntoAnotherShelfOfItsOwn()
    {
        var many = Enumerable.Range(1, 12)
            .Select(n => Book($"Book {n}", "The Long Cycle", n, words: 150_000))
            .ToList();

        var rows = ShelfComposer.Compose(many);

        Assert.True(rows.Count > 1, "Twelve wide spines must not be crammed onto one shelf.");
        // Named by the book that started it, not by the grouping key.
        Assert.All(rows, row => Assert.Equal("Book 1", row.SeriesName));
        Assert.Equal(12, rows.Sum(row => row.Books.Count));
        // Still in order across the wrap.
        Assert.Equal(
            many.Select(book => book.Title),
            rows.SelectMany(row => row.Books).Select(book => book.Title));
    }

    [Fact]
    public void StandaloneBooksShareShelvesAndCarryNoSeriesName()
    {
        var rows = ShelfComposer.Compose([Book("Northing"), Book("Fair Copy"), Book("Weathering")]);

        var row = Assert.Single(rows);
        Assert.Null(row.SeriesName);
        Assert.Equal(3, row.Books.Count);
    }

    [Fact]
    public void SeriesShelvesComeBeforeStandaloneOnes()
    {
        var rows = ShelfComposer.Compose(
        [
            Book("Northing"),
            Book("The Salt Line", "The Salt Line", 1),
        ]);

        Assert.Equal("The Salt Line", rows[0].SeriesName);
        Assert.Null(rows[1].SeriesName);
    }

    [Fact]
    public void AnEmptyLibraryIsAnEmptyShelf()
    {
        Assert.Empty(ShelfComposer.Compose([]));
    }
}
