using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeBoxScreens;

/// <summary>Cook this soon (C-6.2.3, book p.13): next week's plan gets the dish, and the card says so in its own words.</summary>
public class CookSoonTests
{
    private static readonly RecipeId Chili = new("black-bean-chili");

    [Fact]
    public void PlacesItNextWeek()
    {
        var source = new InMemoryBoxSource(BoxFixture.Twelve());

        var state = CookSoon.Press(source, Chili, CookSoonState.Offer);

        Assert.Equal(CookSoonState.Planned, state);
        Assert.Equal([Chili], source.Placements);
    }

    [Fact]
    public void SuccessIsTheCardsOwnWords()
    {
        var source = new InMemoryBoxSource(BoxFixture.Twelve());
        var state = CookSoon.Press(source, Chili, CookSoonState.Offer);

        Assert.Equal("Planned for next week", CookSoon.Words(state));
        Assert.Equal("Cook this soon", CookSoon.Words(CookSoonState.Offer));
        Assert.False(CookSoon.OffersAButton(state));
        Assert.True(CookSoon.OffersAButton(CookSoonState.Offer));
    }

    [Fact]
    public void PressingAPlannedCardPlacesNothingTwice()
    {
        var source = new InMemoryBoxSource(BoxFixture.Twelve());
        CookSoon.Press(source, Chili, CookSoonState.Offer);

        var state = CookSoon.Press(source, Chili, CookSoonState.Planned);

        Assert.Equal(CookSoonState.Planned, state);
        Assert.Single(source.Placements);
    }

    [Fact]
    public void ADishAlreadyInNextWeekReadsPlannedWithoutAPlacement()
    {
        var source = new InMemoryBoxSource(BoxFixture.Twelve());
        source.Planned.Add(Chili);

        Assert.Equal(CookSoonState.Planned, CookSoon.Press(source, Chili, CookSoonState.Offer));
        Assert.Empty(source.Placements);
    }

    [Fact]
    public void WithNoPlannerYetTheCardSaysSoAndStaysOffered()
    {
        var source = new InMemoryBoxSource(BoxFixture.Twelve()) { PlannerExists = false };

        var state = CookSoon.Press(source, Chili, CookSoonState.Offer);

        Assert.Equal(CookSoonState.Unavailable, state);
        Assert.Equal("Planning is not set up yet, so nothing was placed.", CookSoon.Words(state));
        Assert.True(CookSoon.OffersAButton(state));
    }

    [Fact]
    public void ThePlannedStateComesFromWhatTheSourceHolds()
    {
        Assert.Equal(CookSoonState.Planned, CookSoon.StateOf(Chili, new HashSet<RecipeId> { Chili }));
        Assert.Equal(CookSoonState.Offer, CookSoon.StateOf(Chili, new HashSet<RecipeId>()));
    }

    [Fact]
    public void ThePlannedMarkTravelsWithTheCardInTheBox()
    {
        var model = RecipeBoxScreenModel.Build(BoxFixture.Twelve(), null, new HashSet<RecipeId> { Chili });

        Assert.True(model.Piles.SelectMany(pile => pile.Cards).Single(card => card.Id == Chili).IsPlanned);
    }
}
