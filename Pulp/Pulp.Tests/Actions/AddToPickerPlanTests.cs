using Pulp.Domain.Actions;
using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Ratings;

namespace Pulp.Tests.Actions;

/// <summary>
/// The Add to… picker (mockup 11, D254, D257, D258, D255, D109). Invented lists and children.
///
/// <para>The bugs these reintroduce: playlists listed in the wrong order (D109 says last-used first),
/// a blocked child's Favorites that can still be ticked (D255: a block wins), a title above a child's
/// ceiling added without saying it allow-lists it, and an untick that removes without the undo
/// (D257).</para>
/// </summary>
public class AddToPickerPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 20, 0, 0, TimeSpan.Zero);
    private static readonly TitleId Saltmarsh = new("title-saltmarsh");
    private static readonly PlaylistEntry Entry = PlaylistEntry.ForTitle(Saltmarsh, TitleKind.Show);
    private static readonly TitleFacts Teen = new(Saltmarsh, TitleKind.Show, ContentRating.Parse("TV-14"));

    private static PickerPlaylist List(string id, string name, bool shared, DateTimeOffset? used, bool holds = false)
    {
        var playlist = Playlist.Named(name);
        return new PickerPlaylist(id, holds ? playlist.Add(Entry) : playlist, shared, used);
    }

    private static PickerChild Child(string name, AgeBand ceiling) => new(name, new ChildRules(new ViewerId(name.ToLowerInvariant()), ceiling));

    // TV-14 is first admitted at thirteen.
    private static AgeBand BandBelowTeen() => AgeBand.TenToTwelve;

    private static AgeBand BandAtLeastTeen() => AgeBand.ThirteenToSixteen;

    [Fact]
    public void TheHeadingNamesTheTitle()
    {
        var model = AddToPickerPlan.Build("Saltmarsh", Entry, Teen, [], []);

        Assert.Equal("Add Saltmarsh to", model.Heading);
    }

    [Fact]
    public void PlaylistsAreLastUsedFirstThenNeverUsedByName()
    {
        var model = AddToPickerPlan.Build("Saltmarsh", Entry, Teen,
        [
            List("p1", "Mysteries", shared: true, used: null),
            List("p2", "Long winter shows", shared: false, used: Now.AddDays(-3)),
            List("p3", "Rainy Sundays", shared: true, used: Now.AddHours(-1)),
            List("p4", "Comfort films", shared: false, used: null),
        ], []);

        Assert.Equal(
            ["Rainy Sundays", "Long winter shows", "Comfort films", "Mysteries"],
            model.Playlists.Select(row => row.Label));
    }

    [Fact]
    public void APlaylistRowSaysSharedOrYoursAndIsTickedWhenItHoldsTheTitle()
    {
        var model = AddToPickerPlan.Build("Saltmarsh", Entry, Teen,
        [
            List("p1", "Rainy Sundays", shared: true, used: Now, holds: true),
            List("p2", "Mysteries", shared: true, used: null),
            List("p3", "Long winter shows", shared: false, used: Now.AddDays(-1), holds: true),
        ], []);

        var rainy = model.Playlists.Single(row => row.Key == "p1");
        var mysteries = model.Playlists.Single(row => row.Key == "p2");
        var winter = model.Playlists.Single(row => row.Key == "p3");
        Assert.Equal(("shared", true, true), (rainy.Tag, rainy.Ticked, rainy.Enabled));
        Assert.Equal(("shared", false, true), (mysteries.Tag, mysteries.Ticked, mysteries.Enabled));
        Assert.Equal(("yours", true, true), (winter.Tag, winter.Ticked, winter.Enabled));
        Assert.All(model.Playlists, row => Assert.Equal(PickerGroup.Playlists, row.Group));
    }

    [Fact]
    public void AChildAboveTheirBandCarriesTheAboveBandBadgeAndTickingAllowListsIt()
    {
        var model = AddToPickerPlan.Build("Saltmarsh", Entry, Teen, [], [Child("Robin", BandBelowTeen())]);

        var robin = Assert.Single(model.Favorites);
        Assert.Equal(PickerGroup.Favorites, robin.Group);
        Assert.Equal(ChildBadge.AboveBand, robin.Badge);
        Assert.Equal("Above band", AddToPickerPlan.BadgeLabel(robin.Badge));
        Assert.True(robin.Enabled);
        Assert.False(robin.Ticked);
        Assert.Equal(PickerChange.AddAndAllowList, AddToPickerPlan.Tap(robin));
    }

    [Fact]
    public void AChildWithinTheirBandHasNoBadgeAndTickingSimplyAdds()
    {
        var model = AddToPickerPlan.Build("Saltmarsh", Entry, Teen, [], [Child("Robin", BandAtLeastTeen())]);

        var robin = Assert.Single(model.Favorites);
        Assert.Equal(ChildBadge.None, robin.Badge);
        Assert.Null(AddToPickerPlan.BadgeLabel(robin.Badge));
        Assert.Equal(PickerChange.Add, AddToPickerPlan.Tap(robin));
    }

    [Fact]
    public void ABlockedChildIsGreyedAndCannotBeTickedEvenWhenAlreadyAParentsPick()
    {
        var rules = new ChildRules(new ViewerId("juniper"), BandAtLeastTeen())
        {
            BlockedTitles = new HashSet<TitleId> { Saltmarsh },
            ParentPicks = [new ParentPick(Saltmarsh, new ViewerId("parent"), Now)],
        };
        var model = AddToPickerPlan.Build("Saltmarsh", Entry, Teen, [], [new PickerChild("Juniper", rules)]);

        var juniper = Assert.Single(model.Favorites);
        Assert.Equal(ChildBadge.Blocked, juniper.Badge);
        Assert.Equal("Blocked", AddToPickerPlan.BadgeLabel(juniper.Badge));
        Assert.False(juniper.Enabled);
        Assert.Equal(PickerChange.Refused, AddToPickerPlan.Tap(juniper));
    }

    [Fact]
    public void AParentsExistingPickIsTickedAndKeepsItsAboveBandBadge()
    {
        var rules = new ChildRules(new ViewerId("robin"), BandBelowTeen())
        {
            ParentPicks = [new ParentPick(Saltmarsh, new ViewerId("parent"), Now)],
        };
        var model = AddToPickerPlan.Build("Saltmarsh", Entry, Teen, [], [new PickerChild("Robin", rules)]);

        var robin = Assert.Single(model.Favorites);
        Assert.True(robin.Ticked);
        Assert.Equal(ChildBadge.AboveBand, robin.Badge);
        Assert.Equal(PickerChange.RemoveUndoFirst, AddToPickerPlan.Tap(robin));
    }

    [Fact]
    public void AnUnratedTitleReadsAsAboveBandBecauseAChildIsNotShownIt()
    {
        var unrated = new TitleFacts(Saltmarsh, TitleKind.Show, ContentRating.Unrated);
        var model = AddToPickerPlan.Build("Saltmarsh", Entry, unrated, [], [Child("Robin", BandAtLeastTeen())]);

        Assert.Equal(ChildBadge.AboveBand, Assert.Single(model.Favorites).Badge);
    }

    [Fact]
    public void UntickingAPlaylistIsUndoFirstAndTickingAdds()
    {
        var model = AddToPickerPlan.Build("Saltmarsh", Entry, Teen,
        [
            List("p1", "Rainy Sundays", shared: true, used: Now, holds: true),
            List("p2", "Mysteries", shared: true, used: null),
        ], []);

        Assert.Equal(PickerChange.RemoveUndoFirst, AddToPickerPlan.Tap(model.Playlists[0]));
        Assert.Equal(PickerChange.Add, AddToPickerPlan.Tap(model.Playlists[1]));
    }

    [Fact]
    public void AGameHasNoFavoritesGroupBecauseAChildsSportIsByTeam()
    {
        var game = new PlaylistEntry(PlaylistEntryKind.Game, "game-gulls-foxes");
        var model = AddToPickerPlan.Build("Harbor Gulls at Ridge Foxes", game, null,
            [List("p1", "Rainy Sundays", shared: true, used: Now)], [Child("Robin", BandAtLeastTeen())]);

        Assert.Single(model.Playlists);
        Assert.Empty(model.Favorites);
    }

    [Fact]
    public void TheFactsMustBeAboutTheSameTitleAsTheEntry()
    {
        var other = new TitleFacts(new TitleId("title-other"), TitleKind.Show, ContentRating.Parse("TV-14"));

        Assert.Throws<ArgumentException>(() => AddToPickerPlan.Build("Saltmarsh", Entry, other, [], []));
    }

    [Fact]
    public void AFilmOrShowNeedsItsFactsToPlaceItForAChild() =>
        Assert.Throws<ArgumentException>(() => AddToPickerPlan.Build("Saltmarsh", Entry, null, [], [Child("Robin", BandAtLeastTeen())]));

    [Fact]
    public void TheFixedCopyIsTheDrawnCopy()
    {
        Assert.Equal("Playlists", AddToPickerPlan.PlaylistsLabel);
        Assert.Equal("Favorites", AddToPickerPlan.FavoritesLabel);
        Assert.Equal("New playlist…", AddToPickerPlan.NewPlaylistLabel);
    }
}
