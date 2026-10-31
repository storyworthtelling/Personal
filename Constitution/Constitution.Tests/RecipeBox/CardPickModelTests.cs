using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeBoxScreens;

/// <summary>Picking a card out of its pile (C-6.2.2): the selection and the face; the animation is the view's.</summary>
public class CardPickModelTests
{
    private static readonly RecipeId Chili = new("black-bean-chili");
    private static readonly RecipeId Tinga = new("chicken-tinga-tacos");

    [Fact]
    public void ATapPicksTheCardAndTheNextTapOnItsPilePutsItBack()
    {
        var picked = CardSelection.None.Tap(Chili);

        Assert.Equal(Chili, picked.Picked);
        Assert.Null(picked.PutBack().Picked);
        Assert.Null(CardSelection.None.Picked);
    }

    [Fact]
    public void TappingAnotherCardSwapsWhichIsOut()
    {
        Assert.Equal(Tinga, CardSelection.None.Tap(Chili).Tap(Tinga).Picked);
    }

    [Fact]
    public void TappingTheGapOfThePickedCardPutsItBack()
    {
        Assert.Null(CardSelection.None.Tap(Chili).Tap(Chili).Picked);
    }

    [Fact]
    public void TheFaceHasItsHeaderHistoryAndFacts()
    {
        var face = CardPickModel.Face(BoxFixture.Chili(), BoxFixture.ChiliHistory(), false, BoxFixture.Today);

        Assert.Equal("Mexican · family card", face.Header);
        Assert.Equal("Black bean chili", face.Title);
        Assert.Equal("Last cooked 2 weeks ago · loved by Sam, Lee, Robin", face.Headline);
        Assert.Equal("Cooked 6 times since spring", face.Counted);
        Assert.Equal("Sam, Lee, Robin: loved · Juniper: fine", face.Reactions);
        Assert.Equal("10 min hands-on · serves 6", face.Facts);
        Assert.Equal("Cook this soon", face.CookWords);
        Assert.Equal(CookSoonState.Offer, face.Cook);
        Assert.Equal("Tap the pile to put it back", face.PutBackHint);
    }

    [Fact]
    public void ASourceLabelIsNamedInTheHeader()
    {
        var card = BoxFixture.Chili() with { SourceLabel = "Grandma's card" };

        Assert.Equal("Mexican · Grandma's card", CardPickModel.Face(card, CardHistory.Never, false, BoxFixture.Today).Header);
    }

    [Fact]
    public void NoTapeAnywhereInTheFace()
    {
        var face = CardPickModel.Face(BoxFixture.Chili(), BoxFixture.ChiliHistory(), false, BoxFixture.Today);

        Assert.DoesNotContain("tape", string.Join(" ", face.Header, face.Headline, face.PutBackHint), StringComparison.OrdinalIgnoreCase);
    }
}
