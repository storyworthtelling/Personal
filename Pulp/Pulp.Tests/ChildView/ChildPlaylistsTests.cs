using Pulp.Domain.Children;
using Pulp.Domain.ChildView;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using static Pulp.Tests.ChildView.ChildViewFixtures;

namespace Pulp.Tests.ChildView;

/// <summary>
/// A child's Playlists (D42, D97): the playlists every child watching may open, each reduced to the
/// titles every one of them may see (D280). A playlist left with nothing on it is not listed — its
/// name alone would be the one place an adult's list surfaced. Invented household.
/// </summary>
public class ChildPlaylistsTests
{
    private static HouseholdPlaylist List(string id, ViewerId owner, bool shared, params PlaylistEntry[] entries) =>
        new(new PlaylistId(id), owner, shared, entries.Aggregate(Playlist.Named("List " + id), (list, entry) => list.Add(entry)));

    private static PlaylistEntry Show(TitleId title) => PlaylistEntry.ForTitle(title, TitleKind.Show);

    private static TitleFacts? Facts(TitleId title) => title == Grim ? Rated(title, "TV-MA") : Rated(title, "TV-Y");

    [Fact]
    public void AChildsPlaylistShowsOnlyWhatTheChildMaySee()
    {
        var lists = ChildPlaylists.For(
            ChildAudience.Of([RobinRules()]),
            child => [List("p1", Robin, false, Show(Biscuit), Show(Grim), Show(Tidewater))],
            Facts);

        Assert.Equal(new[] { Biscuit, Tidewater }, lists.Single().Titles);
    }

    [Fact]
    public void APlaylistLeftWithNothingIsNotListed()
    {
        var lists = ChildPlaylists.For(
            ChildAudience.Of([RobinRules()]),
            child => [List("p1", Parent, true, Show(Grim))],
            Facts);

        Assert.Empty(lists);
    }

    [Fact]
    public void TogetherOnlyPlaylistsEveryChildMayOpenAreListed()
    {
        var robins = List("p1", Robin, false, Show(Biscuit));
        var shared = List("p2", Parent, true, Show(Tidewater));
        var mine = new Dictionary<ViewerId, HouseholdPlaylist[]> { [Robin] = [robins, shared], [Sam] = [shared] };

        var lists = ChildPlaylists.For(ChildAudience.Of([RobinRules(), SamRules()]), child => mine[child], Facts);

        Assert.Equal(new[] { new PlaylistId("p2") }, lists.Select(list => list.Playlist.Id));
    }

    [Fact]
    public void GamesAndFranchisesAreNotShownOnAChildsPlaylist()
    {
        var lists = ChildPlaylists.For(
            ChildAudience.Of([RobinRules()]),
            child => [List("p1", Robin, false, new PlaylistEntry(PlaylistEntryKind.Game, "game-example"), new PlaylistEntry(PlaylistEntryKind.Franchise, "franchise-example"), Show(Biscuit))],
            Facts);

        Assert.Equal(new[] { Biscuit }, lists.Single().Titles);
    }
}
