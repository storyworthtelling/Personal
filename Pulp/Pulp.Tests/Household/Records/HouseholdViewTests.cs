using Pulp.Domain.Calendar;
using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Friday;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.People;
using Pulp.Domain.Services;
using static Pulp.Tests.Household.Records.RecordsFixtures;

namespace Pulp.Tests.Household.Records;

/// <summary>
/// The household document read back into the Domain's own inputs: the roster, each child's one
/// <see cref="ChildRules"/> with the parents' picks on it, each viewer's own sittings and vetoes,
/// the playlists a viewer may see, the home overrides, and the services.
/// </summary>
public class HouseholdViewTests
{
    private static HouseholdDocument Put<T>(HouseholdDocument document, RecordMap<T> map, T value)
        where T : notnull => HouseholdRecords.Put(document, map, value, Next(), PhoneA);

    [Fact]
    public void AnEmptyHouseholdHasNoRosterRatherThanAnInventedOne()
    {
        var view = HouseholdView.Of(HouseholdDocument.Empty);

        Assert.Null(view.Roster);
        Assert.Empty(view.ChildRules);
        Assert.Empty(view.Unreadable);
    }

    [Fact]
    public void TheRosterIsTheViewerRecords()
    {
        var document = Put(HouseholdDocument.Empty, HouseholdMaps.Viewer, new HouseholdViewer(Robin, false));
        document = Put(document, HouseholdMaps.Viewer, new HouseholdViewer(Pip, true));

        var roster = HouseholdView.Of(document).Roster;

        Assert.NotNull(roster);
        Assert.True(roster!.IsChild(Pip));
        Assert.False(roster.IsChild(Robin));
    }

    [Fact]
    public void AChildsRulesCarryTheParentsPicksFromTheirOwnRecords()
    {
        var document = Put(HouseholdDocument.Empty, HouseholdMaps.ChildRules, new ChildRules(Pip, AgeBand.UnderSeven));
        document = Put(document, HouseholdMaps.ParentPick, new ParentPickEntry(Pip, new ParentPick(Kestrel, Robin, Evening)));
        document = Put(document, HouseholdMaps.ParentPick, new ParentPickEntry(new ViewerId("someone-else-example"), new ParentPick(Lantern, Robin, Evening)));

        var rules = Assert.Single(HouseholdView.Of(document).ChildRules);

        // S4: the pick allow-lists its title through the one ChildRules the filter and Favorites read.
        Assert.Equal(Kestrel, Assert.Single(rules.ParentPicks).Title);
        Assert.Contains(Kestrel, rules.ParentFavorites);
    }

    [Fact]
    public void ARulesRecordWrittenWithPicksDoesNotStoreThemTwice()
    {
        var withPick = new ChildRules(Pip, AgeBand.UnderSeven) { ParentPicks = [new ParentPick(Kestrel, Robin, Evening)] };
        var document = Put(HouseholdDocument.Empty, HouseholdMaps.ChildRules, withPick);

        // Picks live in parent-pick records only; a second copy inside the rules would drift.
        Assert.Empty(Assert.Single(HouseholdView.Of(document).ChildRules).ParentPicks);
    }

    [Fact]
    public void SittingsAreEachViewersOwn()
    {
        var mine = new WatchEvent(Lantern, WatchedKind.Film, Evening, Streamly);
        var theirs = new WatchEvent(Orchard, WatchedKind.Show, Evening, Streamly, new EpisodeRef(1, 1));
        var document = Put(HouseholdDocument.Empty, HouseholdMaps.Watched, new WatchedSitting(Robin, mine));
        document = Put(document, HouseholdMaps.Watched, new WatchedSitting(Avery, theirs));

        var view = HouseholdView.Of(document);

        Assert.Equal(mine, Assert.Single(view.SittingsOf(Robin)));
        Assert.Equal(theirs, Assert.Single(view.SittingsOf(Avery)));
    }

    [Fact]
    public void AViewerSeesTheirOwnPlaylistsAndTheSharedOnes()
    {
        var own = new HouseholdPlaylist(new PlaylistId("pl-own-example"), Robin, false, Playlist.Named("Mine"));
        var shared = new HouseholdPlaylist(new PlaylistId("pl-shared-example"), Avery, true, Playlist.Named("Ours"));
        var private2 = new HouseholdPlaylist(new PlaylistId("pl-private-example"), Avery, false, Playlist.Named("Avery's"));
        var document = HouseholdDocument.Empty;
        foreach (var playlist in new[] { own, shared, private2 })
        {
            document = Put(document, HouseholdMaps.Playlist, playlist);
        }

        var visible = HouseholdView.Of(document).PlaylistsFor(Robin).Select(p => p.Id).ToList();

        Assert.Equal(2, visible.Count);
        Assert.Contains(own.Id, visible);
        Assert.Contains(shared.Id, visible);
    }

    /// <summary>
    /// The one playlist identity: two playlists may share a name, and the calendar's
    /// <see cref="PlaylistShelf"/> is read off the stored playlist rather than kept as a second copy.
    /// </summary>
    [Fact]
    public void TwoPlaylistsWithOneNameAreTwoPlaylistsAndTheShelfIsReadOffThem()
    {
        var first = new HouseholdPlaylist(new PlaylistId("pl-1-example"), Robin, true, Playlist.Named("Weekend"));
        var second = new HouseholdPlaylist(new PlaylistId("pl-2-example"), Robin, false, Playlist.Named("Weekend"));
        var document = Put(Put(HouseholdDocument.Empty, HouseholdMaps.Playlist, first), HouseholdMaps.Playlist, second);

        var view = HouseholdView.Of(document);

        Assert.Equal(2, view.PlaylistsFor(Robin).Count);
        Assert.Equal(new PlaylistShelf("Weekend", Robin, true), view.Playlist(first.Id)!.Shelf);
        Assert.Null(view.Playlist(new PlaylistId("pl-gone-example")));
    }

    [Fact]
    public void AViewersVetoBookHoldsOnlyTheirVetoes()
    {
        var document = Put(HouseholdDocument.Empty, HouseholdMaps.Veto, new ViewerVeto(Robin, new Veto(Kestrel, ["heist"])));
        document = Put(document, HouseholdMaps.Veto, new ViewerVeto(Avery, new Veto(Lantern, [])));

        var book = HouseholdView.Of(document).VetoesOf(Robin);

        Assert.Equal(Robin, book.Viewer);
        Assert.True(book.IsVetoed(Kestrel));
        Assert.False(book.IsVetoed(Lantern));
    }

    [Fact]
    public void HomeOverridesAndServicesComeBack()
    {
        var document = Put(HouseholdDocument.Empty, HouseholdMaps.HomeOverride, new HomeChoice(Orchard, Flicker));
        document = Put(document, HouseholdMaps.HouseholdService, new HouseholdServiceEntry(Streamly));
        document = Put(document, HouseholdMaps.AttestedService, new AttestedServiceEntry(Streamly));

        var view = HouseholdView.Of(document);

        Assert.Equal(Flicker, view.HomeOverrides.For(Orchard));
        Assert.Equal(Streamly, Assert.Single(view.Services));
        var catalogue = new ServiceCatalogue([new StreamingService(Streamly, "Streamly", ["com.example.streamly"], "streamly", [])]);
        Assert.True(view.Attested.Covers(Streamly, catalogue));
    }

    [Fact]
    public void AChildsStarsAreTheirOwn()
    {
        var document = Put(HouseholdDocument.Empty, HouseholdMaps.ChildStar, new ChildStarEntry(Pip, new ChildStar(Lantern, Evening)));
        document = Put(document, HouseholdMaps.ChildStar, new ChildStarEntry(new ViewerId("other-child-example"), new ChildStar(Orchard, Evening)));

        Assert.Equal(Lantern, Assert.Single(HouseholdView.Of(document).StarsOf(Pip)).Title);
    }

    [Fact]
    public void UnreadableRecordsOfEveryKindAreGathered()
    {
        var broken = HouseholdRecord.Write(
            new RecordKey(HouseholdKinds.Viewer, "broken-example"),
            1,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["viewer"] = "broken-example", ["child"] = "perhaps" },
            Next(),
            Tv);

        var view = HouseholdView.Of(HouseholdDocument.Empty.With(broken));

        Assert.Equal(broken.Key, Assert.Single(view.Unreadable).Key);
        Assert.Null(view.Roster);
    }
}
