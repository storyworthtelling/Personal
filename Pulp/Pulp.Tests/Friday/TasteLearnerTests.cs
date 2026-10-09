using Pulp.Domain.Core;
using Pulp.Domain.Friday;
using Pulp.Domain.People;

namespace Pulp.Tests.Friday;

/// <summary>
/// D24: taste is learned passively from what is watched, plus the stack's vetoes — no ratings.
/// D23, D283: each adult's taste is their own viewing; a child's viewing is never tracked, and a child has none.
/// </summary>
public class TasteLearnerTests
{
    private static readonly ViewerId Sam = new("sam");
    private static readonly ViewerId Alex = new("alex");
    private static readonly ViewerId Kit = new("kit");
    private static readonly ViewerId Owner = Sam;
    private static readonly HouseholdRoster Roster = new(
        [new HouseholdViewer(Sam, IsChild: false), new HouseholdViewer(Alex, IsChild: false), new HouseholdViewer(Kit, IsChild: true)]);

    private static WatchedTitle Watched(string id, string tag, params ViewerId[] viewers) =>
        new(new TitleId(id), [tag], viewers);

    [Fact]
    public void WhatTheOwnerWatchesRaisesItsTags()
    {
        var taste = TasteLearner.Learn(Owner, Roster, [Watched("w-1", "noir", Sam)], VetoBook.For(Sam));

        Assert.True(taste.Affinity(["noir"]) > taste.Affinity(["space"]));
    }

    [Fact]
    public void AVetoLowersItsTagsAndBringingItBackUndoesThat()
    {
        var vetoes = VetoBook.For(Sam).Veto(new TitleId("v-1"), ["farce"]);

        Assert.True(TasteLearner.Learn(Owner, Roster, [], vetoes).Affinity(["farce"]) < 0);
        Assert.Equal(0, TasteLearner.Learn(Owner, Roster, [], vetoes.BringBack(new TitleId("v-1"))).Affinity(["farce"]));
    }

    [Fact]
    public void AChildsViewingNeverCounts()
    {
        var taste = TasteLearner.Learn(Owner, Roster, [Watched("w-kid", "puppets", Kit)], VetoBook.For(Sam));

        Assert.Equal(0, taste.Affinity(["puppets"]));
    }

    [Fact]
    public void ViewingWithAChildPresentIsAChildsViewingTooAndDoesNotCount()
    {
        var taste = TasteLearner.Learn(Owner, Roster, [Watched("w-family", "puppets", Sam, Kit)], VetoBook.For(Sam));

        Assert.Equal(0, taste.Affinity(["puppets"]));
    }

    [Fact]
    public void TheOtherAdultsViewingIsTheirsNotTheOwners()
    {
        var taste = TasteLearner.Learn(Owner, Roster, [Watched("w-alex", "rowing", Alex)], VetoBook.For(Sam));

        Assert.Equal(0, taste.Affinity(["rowing"]));
    }

    // D283: each adult's stack is taught by their own viewing — the other adult is not left without one.
    [Fact]
    public void TheOtherAdultLearnsTheirOwnTasteFromTheirOwnViewing()
    {
        var taste = TasteLearner.Learn(
            Alex, Roster, [Watched("w-alex", "rowing", Alex), Watched("w-sam", "noir", Sam)], VetoBook.For(Alex));

        Assert.True(taste.Affinity(["rowing"]) > 0);
        Assert.Equal(0, taste.Affinity(["noir"]));
    }

    [Fact]
    public void AChildHasNoTasteProfileWhateverTheyWatch()
    {
        var taste = TasteLearner.Learn(Kit, Roster, [Watched("w-kid", "puppets", Kit)], VetoBook.For(Kit));

        Assert.Same(TasteProfile.Empty, taste);
    }

    [Fact]
    public void ATitleWatchedTwiceCountsOnce()
    {
        var taste = TasteLearner.Learn(
            Owner, Roster, [Watched("w-1", "noir", Sam), Watched("w-1", "noir", Sam), Watched("w-2", "space", Sam)], VetoBook.For(Sam));

        Assert.Equal(taste.Affinity(["space"]), taste.Affinity(["noir"]));
    }

    // S3: the child is on the roster, so a family sitting counts for nobody's taste — there is no
    // second, loose set of children that could be stale or empty.
    [Fact]
    public void AFamilySittingCountsForNobodysTasteWhenTheChildIsOnTheRoster()
    {
        var family = Watched("w-family", "puppets", Sam, Alex, Kit);

        Assert.Equal(0, TasteLearner.Learn(Sam, Roster, [family], VetoBook.For(Sam)).Affinity(["puppets"]));
        Assert.Equal(0, TasteLearner.Learn(Alex, Roster, [family], VetoBook.For(Alex)).Affinity(["puppets"]));
    }

    // S3: a viewer nobody has said is an adult is refused, not treated as one.
    [Fact]
    public void AViewerNotOnTheRosterIsRefused()
    {
        var stranger = new ViewerId("guest-example");

        Assert.Throws<ArgumentException>(() =>
            TasteLearner.Learn(stranger, Roster, [Watched("w-1", "noir", stranger)], VetoBook.For(stranger)));
    }

    // S3: an unlisted co-viewer may be a child nobody added; the sitting is refused rather than counted.
    [Fact]
    public void ASittingWithAViewerNotOnTheRosterIsRefused() =>
        Assert.Throws<ArgumentException>(() =>
            TasteLearner.Learn(Sam, Roster, [Watched("w-1", "puppets", Sam, new ViewerId("guest-example"))], VetoBook.For(Sam)));
}
