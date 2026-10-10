using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Friday;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.People;
using static Pulp.Tests.Household.Records.RecordsFixtures;

namespace Pulp.Tests.Household.Records;

/// <summary>
/// Every record kind goes into the household document and comes back as the same Domain value. A
/// field that does not round-trip is a field one phone writes and the other never sees — a
/// stopped-at minute lost, a child's block dropped — with nothing failing.
/// </summary>
public class HouseholdRecordsRoundTripTests
{
    private static T RoundTrip<T>(RecordMap<T> map, T value)
        where T : notnull
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, map, value, Next(), PhoneA);
        var read = HouseholdRecords.Read(document, map);
        Assert.Empty(read.Unreadable);
        return Assert.Single(read.Items);
    }

    [Fact]
    public void ASessionSittingKeepsEveryObservation()
    {
        var sitting = new WatchedSitting(Robin, new WatchEvent(
            Orchard, WatchedKind.Show, Evening, Streamly, new EpisodeRef(2, 7), WatchSource.Session,
            StoppedAt: TimeSpan.FromSeconds(1234.5), Runtime: TimeSpan.FromMinutes(52), Ended: true));

        var back = RoundTrip(HouseholdMaps.Watched, sitting);

        Assert.Equal(sitting, back);
        Assert.Equal(Evening.Offset, back.Sitting.At.Offset);
    }

    [Theory]
    [InlineData(WatchSource.HandOff, WatchedKind.Film)]
    [InlineData(WatchSource.MarkedWatched, WatchedKind.Show)]
    [InlineData(WatchSource.MarkedUnwatched, WatchedKind.Film)]
    [InlineData(WatchSource.Session, WatchedKind.Game)]
    public void EverySourceAndKindRoundTrips(WatchSource source, WatchedKind kind)
    {
        var sitting = new WatchedSitting(Avery, new WatchEvent(Lantern, kind, Evening, Service: null, Evidence: source));

        Assert.Equal(sitting, RoundTrip(HouseholdMaps.Watched, sitting));
    }

    [Fact]
    public void APlaylistKeepsItsIdOwnerSharingAndHandOrder()
    {
        var list = Playlist.Named("Rainy Sundays")
            .Add(new PlaylistEntry(PlaylistEntryKind.Show, Orchard.Value))
            .Add(new PlaylistEntry(PlaylistEntryKind.Film, Lantern.Value))
            .Add(new PlaylistEntry(PlaylistEntryKind.Franchise, "harbour, lights & co/1"))
            .Add(new PlaylistEntry(PlaylistEntryKind.Game, "game-77-example"));
        list = list.Move(list.Entries[2], 0);
        var playlist = new HouseholdPlaylist(new PlaylistId("pl-0001-example"), Robin, Shared: true, list);

        var back = RoundTrip(HouseholdMaps.Playlist, playlist);

        Assert.Equal(playlist.Id, back.Id);
        Assert.Equal(Robin, back.Owner);
        Assert.True(back.Shared);
        Assert.Equal("Rainy Sundays", back.List.Name);
        Assert.Equal(list.Entries, back.List.Entries);
    }

    [Fact]
    public void AnEmptyPlaylistRoundTrips()
    {
        var playlist = new HouseholdPlaylist(new PlaylistId("pl-empty-example"), Avery, Shared: false, Playlist.Named("Later"));

        var back = RoundTrip(HouseholdMaps.Playlist, playlist);

        Assert.Empty(back.List.Entries);
        Assert.False(back.Shared);
    }

    [Fact]
    public void AVetoKeepsItsTags()
    {
        var veto = new ViewerVeto(Robin, new Veto(Kestrel, ["heist", "slow, burn", ""]));

        var back = RoundTrip(HouseholdMaps.Veto, veto);

        Assert.Equal(Robin, back.Viewer);
        Assert.Equal(Kestrel, back.Veto.Title);
        // An empty tag carries no meaning and is not written.
        Assert.Equal(["heist", "slow, burn"], back.Veto.Tags);
    }

    [Fact]
    public void AChildsStarAndAParentsPickRoundTrip()
    {
        var star = new ChildStarEntry(Pip, new ChildStar(Lantern, Evening));
        var pick = new ParentPickEntry(Pip, new ParentPick(Orchard, Robin, Evening.AddDays(1)));

        Assert.Equal(star, RoundTrip(HouseholdMaps.ChildStar, star));
        Assert.Equal(pick, RoundTrip(HouseholdMaps.ParentPick, pick));
    }

    [Fact]
    public void AHomeOverrideRoundTrips()
    {
        var choice = new HomeChoice(Orchard, Flicker);

        Assert.Equal(choice, RoundTrip(HouseholdMaps.HomeOverride, choice));
    }

    [Fact]
    public void ChildRulesKeepEveryBlockAndTheCeiling()
    {
        var rules = new ChildRules(Pip, AgeBand.SevenToNine)
        {
            Allowed = new HashSet<TitleId> { Kestrel },
            BlockedTitles = new HashSet<TitleId> { Lantern, Orchard },
            BlockedFranchises = new HashSet<FranchiseId> { new("franchise-example") },
            BlockedServices = new HashSet<ServiceId> { Flicker },
        };

        var back = RoundTrip(HouseholdMaps.ChildRules, rules);

        Assert.Equal(Pip, back.Child);
        Assert.Equal(AgeBand.SevenToNine, back.Ceiling);
        Assert.True(back.Allowed.SetEquals(rules.Allowed));
        Assert.True(back.BlockedTitles.SetEquals(rules.BlockedTitles));
        Assert.True(back.BlockedFranchises.SetEquals(rules.BlockedFranchises));
        Assert.True(back.BlockedServices.SetEquals(rules.BlockedServices));
    }

    [Theory]
    [InlineData(AgeBand.UnderSeven)]
    [InlineData(AgeBand.SevenToNine)]
    [InlineData(AgeBand.TenToTwelve)]
    [InlineData(AgeBand.ThirteenToSixteen)]
    [InlineData(AgeBand.Seventeen)]
    public void EveryCeilingRoundTrips(AgeBand band) =>
        Assert.Equal(band, RoundTrip(HouseholdMaps.ChildRules, new ChildRules(Pip, band)).Ceiling);

    [Fact]
    public void ARosterViewerAndTheServicesRoundTrip()
    {
        Assert.Equal(new HouseholdViewer(Pip, IsChild: true), RoundTrip(HouseholdMaps.Viewer, new HouseholdViewer(Pip, IsChild: true)));
        Assert.Equal(new HouseholdViewer(Robin, IsChild: false), RoundTrip(HouseholdMaps.Viewer, new HouseholdViewer(Robin, IsChild: false)));
        Assert.Equal(new AttestedServiceEntry(Streamly), RoundTrip(HouseholdMaps.AttestedService, new AttestedServiceEntry(Streamly)));
        Assert.Equal(new HouseholdServiceEntry(Flicker), RoundTrip(HouseholdMaps.HouseholdService, new HouseholdServiceEntry(Flicker)));
    }

    [Fact]
    public void AStoredWordIsAWordNotAnEnumName()
    {
        // Enum names are code; the file outlives a rename. The words are the map's own.
        var document = HouseholdRecords.Put(
            HouseholdDocument.Empty,
            HouseholdMaps.Watched,
            new WatchedSitting(Robin, new WatchEvent(Lantern, WatchedKind.Film, Evening, null, Evidence: WatchSource.MarkedUnwatched)),
            Next(),
            PhoneA);

        var fields = Assert.Single(document.Records).Fields;
        Assert.Equal("film", fields["kind"]);
        Assert.Equal("marked-unwatched", fields["evidence"]);
    }
}
