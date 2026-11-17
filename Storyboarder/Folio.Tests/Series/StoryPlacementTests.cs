using Folio.Domain.Series;
using Xunit;

namespace Folio.Tests.Series;

// Where a book sits in its series (release G, R21), with invented titles.
public class StoryPlacementTests
{
    [Fact]
    public void APlacementSaysWhereTheBookSits()
    {
        Assert.Equal("after “The Salt Road”", StoryPlacement.After("The Salt Road").Where);
        Assert.Equal("before “The Salt Road”", StoryPlacement.Before("The Salt Road").Where);
        Assert.Equal("between “The Salt Road” and “The Tide Ledger”",
            StoryPlacement.Between("The Salt Road", "The Tide Ledger").Where);
        Assert.Equal("in the age before the flood", StoryPlacement.InEra("the age before the flood").Where);
        Assert.Equal("in the third age", StoryPlacement.InEra("in the third age").Where);
    }

    [Fact]
    public void AGuessSaysItIsOneAndAConfirmedPlacementDoesNot()
    {
        var guess = StoryPlacement.After("The Salt Road");

        Assert.Equal("Takes place after “The Salt Road” · Folio’s guess", guess.Line);
        Assert.Contains("hasn’t been confirmed", guess.Assumption);
        Assert.Equal("Takes place after “The Salt Road”", guess.Confirm().Line);
        Assert.Equal(string.Empty, guess.Confirm().Assumption);
    }

    [Fact]
    public void TheChoicesAreAfterOrBeforeEachBookAndBetweenNeighbours()
    {
        var choices = StoryPlacement.Choices(["The Salt Road", "The Tide Ledger"]).Select(choice => choice.Where).ToList();

        Assert.Equal(
        [
            "after “The Salt Road”",
            "after “The Tide Ledger”",
            "before “The Salt Road”",
            "before “The Tide Ledger”",
            "between “The Salt Road” and “The Tide Ledger”",
        ], choices);
        Assert.Empty(StoryPlacement.Choices([]));
    }

    [Fact]
    public void ThePlacementRoundTripsAndDamageIsNoPlacement()
    {
        var placement = StoryPlacement.Between("The Salt Road", "The Tide Ledger").Confirm();

        Assert.Equal(placement, StoryPlacementCodec.Read(StoryPlacementCodec.Write(placement)));
        Assert.Contains("\"between\"", StoryPlacementCodec.Write(placement), StringComparison.OrdinalIgnoreCase);
        Assert.Null(StoryPlacementCodec.Read(null));
        Assert.Null(StoryPlacementCodec.Read("not json"));
    }

    [Fact]
    public void TheCardLeadsWithTheGuessThenOffersEveryOtherPlaceAndAnEra()
    {
        var guess = StoryPlacement.After("The Salt Road");

        var options = PlacementCard.Options(guess, ["The Salt Road", "The Tide Ledger"]);

        Assert.Equal("Yes, after \u201CThe Salt Road\u201D", options[0].Label);
        Assert.Same(guess, options[0].Placement);
        Assert.DoesNotContain(options.Skip(1), option => option.Placement?.Where == guess.Where);
        Assert.Contains(options, option => option.Label == "Before \u201CThe Tide Ledger\u201D");
        Assert.Equal(PlacementCard.EraOption, options[^1].Label);
        Assert.Null(options[^1].Placement);
        Assert.Equal("Folio guessed after \u201CThe Salt Road\u201D.", PlacementCard.Subtitle(guess));
    }

    [Fact]
    public void ForAConfirmedPlacementTheCardOffersOnlyTheOtherPlaces()
    {
        var confirmed = StoryPlacement.After("The Salt Road").Confirm();

        var options = PlacementCard.Options(confirmed, ["The Salt Road"]);

        Assert.Equal(["Before \u201CThe Salt Road\u201D", PlacementCard.EraOption], options.Select(option => option.Label));
        Assert.Null(PlacementCard.Subtitle(confirmed));
    }
}
