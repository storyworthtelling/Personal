using Pulp.Domain.Core;
using Pulp.Domain.Friday;

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
    private static readonly StackViewer Owner = new(Sam, IsChild: false);
    private static readonly HashSet<ViewerId> Children = [Kit];

    private static WatchedTitle Watched(string id, string tag, params ViewerId[] viewers) =>
        new(new TitleId(id), [tag], viewers);

    [Fact]
    public void WhatTheOwnerWatchesRaisesItsTags()
    {
        var taste = TasteLearner.Learn(Owner, Children, [Watched("w-1", "noir", Sam)], VetoBook.For(Sam));

        Assert.True(taste.Affinity(["noir"]) > taste.Affinity(["space"]));
    }

    [Fact]
    public void AVetoLowersItsTagsAndBringingItBackUndoesThat()
    {
        var vetoes = VetoBook.For(Sam).Veto(new TitleId("v-1"), ["farce"]);

        Assert.True(TasteLearner.Learn(Owner, Children, [], vetoes).Affinity(["farce"]) < 0);
        Assert.Equal(0, TasteLearner.Learn(Owner, Children, [], vetoes.BringBack(new TitleId("v-1"))).Affinity(["farce"]));
    }

    [Fact]
    public void AChildsViewingNeverCounts()
    {
        var taste = TasteLearner.Learn(Owner, Children, [Watched("w-kid", "puppets", Kit)], VetoBook.For(Sam));

        Assert.Equal(0, taste.Affinity(["puppets"]));
    }

    [Fact]
    public void ViewingWithAChildPresentIsAChildsViewingTooAndDoesNotCount()
    {
        var taste = TasteLearner.Learn(Owner, Children, [Watched("w-family", "puppets", Sam, Kit)], VetoBook.For(Sam));

        Assert.Equal(0, taste.Affinity(["puppets"]));
    }

    [Fact]
    public void TheOtherAdultsViewingIsTheirsNotTheOwners()
    {
        var taste = TasteLearner.Learn(Owner, Children, [Watched("w-alex", "rowing", Alex)], VetoBook.For(Sam));

        Assert.Equal(0, taste.Affinity(["rowing"]));
    }

    // D283: each adult's stack is taught by their own viewing — the other adult is not left without one.
    [Fact]
    public void TheOtherAdultLearnsTheirOwnTasteFromTheirOwnViewing()
    {
        var alex = new StackViewer(Alex, IsChild: false);
        var taste = TasteLearner.Learn(
            alex, Children, [Watched("w-alex", "rowing", Alex), Watched("w-sam", "noir", Sam)], VetoBook.For(Alex));

        Assert.True(taste.Affinity(["rowing"]) > 0);
        Assert.Equal(0, taste.Affinity(["noir"]));
    }

    [Fact]
    public void AChildHasNoTasteProfileWhateverTheyWatch()
    {
        var child = new StackViewer(Kit, IsChild: true);

        var taste = TasteLearner.Learn(child, Children, [Watched("w-kid", "puppets", Kit)], VetoBook.For(Kit));

        Assert.Same(TasteProfile.Empty, taste);
    }

    [Fact]
    public void ATitleWatchedTwiceCountsOnce()
    {
        var taste = TasteLearner.Learn(
            Owner, Children, [Watched("w-1", "noir", Sam), Watched("w-1", "noir", Sam), Watched("w-2", "space", Sam)], VetoBook.For(Sam));

        Assert.Equal(taste.Affinity(["space"]), taste.Affinity(["noir"]));
    }
}
