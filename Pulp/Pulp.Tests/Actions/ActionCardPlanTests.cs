using Pulp.Domain.Actions;

namespace Pulp.Tests.Actions;

/// <summary>
/// The action card (mockup 10, D256): which actions a long-press offers, by where it was pressed.
/// Invented titles only.
///
/// <para>The bugs these reintroduce: Plan to watch creeping back onto the card (D251), a child's card
/// offering an adult's actions (D230, D253), a destructive removal that happens without the undo
/// dog-ear (§13), and a card that asks "are you sure?" instead of doing the thing (§7).</para>
/// </summary>
public class ActionCardPlanTests
{
    private static CardItem ContinueRow() =>
        new("Saltmarsh", CardPlace.ContinueWatching)
        {
            Subtitle = "S1 · E3 · Low Water · Northstar+",
            NextUp = "S1 · E3",
            NextEpisode = "E3",
        };

    [Fact]
    public void ContinueWatchingOffersTheFourDrawnActionsInOrder()
    {
        var plan = ActionCardPlan.For(ContinueRow());

        Assert.Equal("Saltmarsh", plan.Title);
        Assert.Equal("S1 · E3 · Low Water · Northstar+", plan.Subtitle);
        Assert.Equal(
            ["Play S1 · E3", "Mark E3 watched", "Add to…", "Remove from Continue watching"],
            plan.Entries.Select(entry => entry.Label));
        Assert.Equal(
            [CardActionKind.Play, CardActionKind.MarkWatched, CardActionKind.AddTo, CardActionKind.RemoveFromContinueWatching],
            plan.Entries.Select(entry => entry.Kind));
    }

    [Fact]
    public void AFilmOnContinueWatchingPlaysAndMarksWithoutAnEpisode()
    {
        var plan = ActionCardPlan.For(new CardItem("The Long Way to Larkspur", CardPlace.ContinueWatching));

        Assert.Equal("Play", plan.Entries[0].Label);
        Assert.Equal("Mark watched", plan.Entries[1].Label);
    }

    [Fact]
    public void APlaylistRowOffersPlayMarkMoveRemove()
    {
        var plan = ActionCardPlan.For(new CardItem("Foxglove Station", CardPlace.PlaylistRow));

        Assert.Equal(
            ["Play", "Mark watched", "Move to another playlist", "Remove from this playlist"],
            plan.Entries.Select(entry => entry.Label));
    }

    [Fact]
    public void RecentlyWatchedOffersUnwatchAgainAndRemoveFromHistory()
    {
        var plan = ActionCardPlan.For(new CardItem("Foxglove Station", CardPlace.RecentlyWatched));

        Assert.Equal(
            ["Mark unwatched", "Watch again", "Remove from history"],
            plan.Entries.Select(entry => entry.Label));
    }

    [Theory]
    [InlineData(false, "Watched")]
    [InlineData(true, "Unwatched")]
    public void AnEpisodeOffersTheMarkThatChangesItAndWatchedUpToHere(bool watched, string mark)
    {
        var plan = ActionCardPlan.For(new CardItem("Saltmarsh", CardPlace.Episode) { EpisodeWatched = watched });

        Assert.Equal([mark, "Watched up to here"], plan.Entries.Select(entry => entry.Label));
    }

    [Fact]
    public void AGameOnTheSportsPageOffersPlayMarkAndAddTo()
    {
        var plan = ActionCardPlan.For(new CardItem("Harbor Gulls at Ridge Foxes", CardPlace.SportsGame));

        Assert.Equal(["Play", "Mark watched", "Add to…"], plan.Entries.Select(entry => entry.Label));
    }

    [Theory]
    [InlineData(CardPlace.ContinueWatching)]
    [InlineData(CardPlace.PlaylistRow)]
    [InlineData(CardPlace.RecentlyWatched)]
    [InlineData(CardPlace.Episode)]
    public void AChildSeesOnlyPlayAndTheStar(CardPlace place)
    {
        var plan = ActionCardPlan.For(new CardItem("Saltmarsh", place) { ChildView = true, NextUp = "S1 · E3" });

        Assert.Equal([CardActionKind.Play, CardActionKind.Favorite], plan.Entries.Select(entry => entry.Kind));
        Assert.Equal("★ Favorite", plan.Entries[1].Label);
    }

    [Fact]
    public void AChildWhoAlreadyStarredItCanTakeTheStarBackUndoFirst()
    {
        var plan = ActionCardPlan.For(
            new CardItem("Saltmarsh", CardPlace.ContinueWatching) { ChildView = true, ChildStarred = true });

        var unstar = Assert.Single(plan.Entries, entry => entry.Kind == CardActionKind.Unfavorite);
        Assert.True(unstar.UndoFirst);
        Assert.DoesNotContain(plan.Entries, entry => entry.Kind == CardActionKind.Favorite);
    }

    [Fact]
    public void AChildsGameIsPlayOnly()
    {
        var plan = ActionCardPlan.For(new CardItem("Harbor Gulls at Ridge Foxes", CardPlace.SportsGame) { ChildView = true });

        Assert.Equal([CardActionKind.Play], plan.Entries.Select(entry => entry.Kind));
    }

    [Theory]
    [InlineData(CardActionKind.RemoveFromContinueWatching)]
    [InlineData(CardActionKind.RemoveFromPlaylist)]
    [InlineData(CardActionKind.RemoveFromHistory)]
    [InlineData(CardActionKind.MarkWatched)]
    [InlineData(CardActionKind.MarkUnwatched)]
    [InlineData(CardActionKind.Watched)]
    [InlineData(CardActionKind.Unwatched)]
    [InlineData(CardActionKind.WatchedUpToHere)]
    public void EveryRemovalAndEveryManualMarkIsUndoFirst(CardActionKind kind)
    {
        var entries = AllPlans().SelectMany(plan => plan.Entries).Where(entry => entry.Kind == kind).ToList();

        Assert.NotEmpty(entries);
        Assert.All(entries, entry => Assert.True(entry.UndoFirst, $"{entry.Label} must fold an undo dog-ear (§13, D160)."));
    }

    [Theory]
    [InlineData(CardActionKind.Play)]
    [InlineData(CardActionKind.AddTo)]
    [InlineData(CardActionKind.MoveToPlaylist)]
    [InlineData(CardActionKind.WatchAgain)]
    [InlineData(CardActionKind.Favorite)]
    public void NothingThatOnlyOpensOrAddsFoldsAnUndo(CardActionKind kind)
    {
        var entries = AllPlans().SelectMany(plan => plan.Entries).Where(entry => entry.Kind == kind).ToList();

        Assert.NotEmpty(entries);
        Assert.All(entries, entry => Assert.False(entry.UndoFirst));
    }

    [Fact]
    public void NoCardOffersPlanToWatchOrAConfirmation()
    {
        var labels = AllPlans().SelectMany(plan => plan.Entries).Select(entry => entry.Label).ToList();

        Assert.True(labels.Count > 10, "The plans under test came back nearly empty; this guard would pass on nothing.");
        Assert.DoesNotContain(labels, label => label.Contains("Plan", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(labels, label => label.Contains("want this", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(labels, label => label.Contains("sure", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(labels, label => label.Equals("Cancel", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ATitleIsRequired() =>
        Assert.Throws<ArgumentException>(() => ActionCardPlan.For(new CardItem(" ", CardPlace.PlaylistRow)));

    private static IEnumerable<CardPlan> AllPlans()
    {
        foreach (var place in Enum.GetValues<CardPlace>())
        {
            foreach (var child in new[] { false, true })
            {
                foreach (var flag in new[] { false, true })
                {
                    yield return ActionCardPlan.For(new CardItem("Saltmarsh", place)
                    {
                        ChildView = child,
                        ChildStarred = flag,
                        EpisodeWatched = flag,
                        NextUp = flag ? "S1 · E3" : null,
                        NextEpisode = flag ? "E3" : null,
                    });
                }
            }
        }
    }
}
