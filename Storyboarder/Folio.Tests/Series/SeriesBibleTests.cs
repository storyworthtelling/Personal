using Folio.Domain.Analysis;
using Folio.Domain.Series;
using Xunit;

namespace Folio.Tests.Series;

// The series bible (release G, R20, R23, R24, R26), with an invented series: "The Salt Road" and its sequel.
public class SeriesBibleTests
{
    private static readonly Guid BookOne = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BookTwo = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static BibleFact Fact(BibleKind kind, string name, string text, string quote = "", string era = "") =>
        new(kind, name, [], era, text, quote);

    [Fact]
    public void ABooksFactsBecomeEntriesEachWithItsBooksState()
    {
        var bible = SeriesBible.Empty.WithBook(new SeriesBook(BookOne, "The Salt Road", 1),
        [
            Fact(BibleKind.Character, "Wren Abernay", "A ferry pilot with a scarred left hand.", "her scarred left hand"),
            Fact(BibleKind.Setting, "Gullhaven", "A harbour town built on stilts."),
        ]);

        var wren = Assert.Single(bible.Entries, entry => entry.Kind == BibleKind.Character);
        var state = Assert.Single(wren.States);
        Assert.Equal(BookOne, state.BookId);
        Assert.Equal("The Salt Road", state.Book);
        Assert.Equal("her scarred left hand", state.Quote);
        Assert.Equal("A ferry pilot with a scarred left hand.", wren.Current);
        Assert.Single(bible.Books);
    }

    [Fact]
    public void AReturningEntryGainsTheNewBooksStateMatchedByNameOrAliasWithoutCase()
    {
        var bible = SeriesBible.Empty
            .WithBook(new SeriesBook(BookOne, "The Salt Road", 1),
                [new BibleFact(BibleKind.Character, "Wren Abernay", ["the pilot"], "", "A ferry pilot.", "")])
            .WithBook(new SeriesBook(BookTwo, "The Tide Ledger", 2),
                [new BibleFact(BibleKind.Character, "THE PILOT", ["Wren"], "Ten years on", "Now a harbourmaster.", "")]);

        var wren = Assert.Single(bible.Entries);
        Assert.Equal(["The Salt Road", "The Tide Ledger"], wren.States.Select(state => state.Book));
        Assert.Contains("Wren", wren.Aliases);
        Assert.Equal("Now a harbourmaster.", wren.Current);
        Assert.Equal("Ten years on", wren.States[1].Era);
    }

    [Fact]
    public void TheEarlierWrittenBookIsCanonWhateverOrderTheBooksWereRead()
    {
        // Book two read into the bible first, book one afterwards: the established state is still book one's.
        var bible = SeriesBible.Empty
            .WithBook(new SeriesBook(BookTwo, "The Tide Ledger", 2), [Fact(BibleKind.Setting, "Gullhaven", "Flooded.")])
            .WithBook(new SeriesBook(BookOne, "The Salt Road", 1), [Fact(BibleKind.Setting, "Gullhaven", "Built on stilts.")]);

        var gullhaven = Assert.Single(bible.Entries);
        Assert.Equal("The Salt Road", gullhaven.Established!.Book);
        Assert.Equal(["The Salt Road", "The Tide Ledger"], bible.Books.Select(book => book.Title));
    }

    [Fact]
    public void ReadingABookAgainReplacesItsStatesAndDropsWhatItNoLongerSays()
    {
        var bible = SeriesBible.Empty
            .WithBook(new SeriesBook(BookOne, "The Salt Road", 1),
                [Fact(BibleKind.Character, "Wren Abernay", "Scarred."), Fact(BibleKind.Character, "Oslo Crane", "A smuggler.")])
            .WithBook(new SeriesBook(BookOne, "The Salt Road", 1), [Fact(BibleKind.Character, "Wren Abernay", "Unscarred.")]);

        var wren = Assert.Single(bible.Entries);
        Assert.Equal("Unscarred.", Assert.Single(wren.States).Text);
    }

    [Fact]
    public void AnOwnersCorrectionWinsOverTheBooksAndSurvivesAnotherRead()
    {
        var bible = SeriesBible.Empty
            .WithBook(new SeriesBook(BookOne, "The Salt Road", 1), [Fact(BibleKind.Character, "Wren Abernay", "Scarred left hand.")])
            .Correct(BibleKind.Character, "wren abernay", "Scarred right hand.")
            .WithBook(new SeriesBook(BookTwo, "The Tide Ledger", 2), [Fact(BibleKind.Character, "Wren Abernay", "Left hand healed.")]);

        var wren = Assert.Single(bible.Entries);
        Assert.Equal("Scarred right hand.", wren.Current);
        Assert.Equal("Scarred right hand.", wren.Correction);
        Assert.Equal(2, wren.States.Count);
    }

    [Fact]
    public void ACorrectionCanBeClearedAndANameNobodyHasIsIgnored()
    {
        var bible = SeriesBible.Empty
            .WithBook(new SeriesBook(BookOne, "The Salt Road", 1), [Fact(BibleKind.Setting, "Gullhaven", "On stilts.")])
            .Correct(BibleKind.Setting, "Gullhaven", "On piles.")
            .Correct(BibleKind.Setting, "Gullhaven", "  ")
            .Correct(BibleKind.Setting, "Nowhere", "Invented.");

        var gullhaven = Assert.Single(bible.Entries);
        Assert.Equal(string.Empty, gullhaven.Correction);
        Assert.Equal("On stilts.", gullhaven.Current);
    }

    [Fact]
    public void WithoutABookLeavesOnlyWhatTheOtherBooksEstablished()
    {
        var bible = SeriesBible.Empty
            .WithBook(new SeriesBook(BookOne, "The Salt Road", 1), [Fact(BibleKind.Character, "Wren Abernay", "Scarred.")])
            .WithBook(new SeriesBook(BookTwo, "The Tide Ledger", 2),
                [Fact(BibleKind.Character, "Wren Abernay", "Healed."), Fact(BibleKind.Thread, "The missing ledger", "Never found.")]);

        var earlier = bible.Without(BookTwo);

        var wren = Assert.Single(earlier.Entries);
        Assert.Equal("Scarred.", Assert.Single(wren.States).Text);
        Assert.Equal(["The Salt Road"], earlier.Books.Select(book => book.Title));
        Assert.True(bible.Has(BookTwo));
        Assert.False(earlier.Has(BookTwo));
    }

    [Fact]
    public void AFactWithNoNameOrNoTextIsDropped()
    {
        var bible = SeriesBible.Empty.WithBook(new SeriesBook(BookOne, "The Salt Road", 1),
            [Fact(BibleKind.Character, " ", "Someone."), Fact(BibleKind.Character, "Wren Abernay", "")]);

        Assert.Empty(bible.Entries);
        Assert.Single(bible.Books);
    }

    [Fact]
    public void TheCodecRoundTripsTheWholeBible()
    {
        var bible = SeriesBible.Empty
            .WithBook(new SeriesBook(BookOne, "The Salt Road", 1),
                [new BibleFact(BibleKind.Rule, "Salt binds a promise", ["salt-oath"], "Founding era", "Broken oaths drown.", "the salt remembers")])
            .Correct(BibleKind.Rule, "Salt binds a promise", "Broken oaths drown at the next tide.");

        var read = SeriesBibleCodec.Read(SeriesBibleCodec.Write(bible));

        var rule = Assert.Single(read.Entries);
        Assert.Equal(BibleKind.Rule, rule.Kind);
        Assert.Equal(["salt-oath"], rule.Aliases);
        Assert.Equal("the salt remembers", rule.States[0].Quote);
        Assert.Equal("Founding era", rule.States[0].Era);
        Assert.Equal("Broken oaths drown at the next tide.", rule.Current);
        Assert.Equal(bible.Books, read.Books);
    }

    [Fact]
    public void TheCodecReadsNothingOrDamageAsAnEmptyBible()
    {
        Assert.Empty(SeriesBibleCodec.Read(null).Entries);
        Assert.Empty(SeriesBibleCodec.Read("not json").Entries);
        Assert.True(SeriesBibleCodec.TryRead(null, out _));
        Assert.False(SeriesBibleCodec.TryRead("not json", out _));
        Assert.False(SeriesBibleCodec.TryRead("{}", out _));
    }

    [Fact]
    public void AnEarlierBooksStoredCanonSeedsTheBibleWithoutReadingItAgain()
    {
        var canon = new SeriesCanon(
            "The Salt Road",
            [new CanonEntity("Wren Abernay", "A ferry pilot.")],
            [new CanonEntity("Gullhaven", "A harbour town on stilts.")],
            "Wren has sworn the salt-oath and the ledger is lost.",
            [new ReportFinding("Ch. 9", "Who sank the ferry is never said.")]);

        var bible = SeriesBible.Empty.WithBook(new SeriesBook(BookOne, "The Salt Road", 1), BibleSeed.From(canon));

        Assert.Contains(bible.Entries, entry => entry.Kind == BibleKind.Character && entry.Name == "Wren Abernay");
        Assert.Contains(bible.Entries, entry => entry.Kind == BibleKind.Setting && entry.Name == "Gullhaven");
        Assert.Contains(bible.Entries, entry => entry.Kind == BibleKind.Thread && entry.Current == "Who sank the ferry is never said.");
        Assert.Contains(bible.Entries, entry => entry.Kind == BibleKind.Event && entry.Current.Contains("salt-oath"));
        Assert.All(bible.Entries, entry => Assert.Equal(string.Empty, entry.States[0].Quote));
    }

    [Fact]
    public void AnEmptyCanonSeedsNothing()
    {
        Assert.Empty(BibleSeed.From(SeriesCanon.Empty));
    }

    [Fact]
    public void AQuoteTheBookDoesNotContainIsDroppedButTheFactKept()
    {
        const string manuscript = "Wren held the tiller with her scarred\nleft hand and did not look back.";
        var facts = BibleQuotes.Keep(manuscript,
        [
            Fact(BibleKind.Character, "Wren Abernay", "Scarred left hand.", "her scarred left hand"),
            Fact(BibleKind.Character, "Oslo Crane", "A smuggler.", "Oslo grinned at the customs man"),
        ]);

        Assert.Equal("her scarred left hand", facts[0].Quote);
        Assert.Equal(string.Empty, facts[1].Quote);
        Assert.Equal("A smuggler.", facts[1].Text);
    }

    [Fact]
    public void TheBibleReadsAsTextForAPromptWithEachEntrysBooksAndQuotes()
    {
        var bible = SeriesBible.Empty
            .WithBook(new SeriesBook(BookOne, "The Salt Road", 1),
                [Fact(BibleKind.Character, "Wren Abernay", "Scarred left hand.", "her scarred left hand", "Year of the flood")])
            .Correct(BibleKind.Character, "Wren Abernay", "Scarred right hand.");

        var text = BibleText.For(bible);

        Assert.Contains("CHARACTERS", text);
        Assert.Contains("Wren Abernay", text);
        Assert.Contains("\"The Salt Road\"", text);
        Assert.Contains("Year of the flood", text);
        Assert.Contains("her scarred left hand", text);
        Assert.Contains("Scarred right hand.", text);
        Assert.Contains("the author's correction", text);
        Assert.Equal(string.Empty, BibleText.For(SeriesBible.Empty));
    }
}
