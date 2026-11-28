using Watch.Domain.Dash;
using Watch.Domain.Design;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// The People waiting tile (P14 B8, W-5.1.7, A72; mock: Tile: People waiting, Tile: nobody waiting): the dash, the newest
/// name in full and the two before it fading, no count however many wait; nobody waiting is the faint dash and nothing
/// else; a phone out of reach is said, never drawn as nobody.
/// </summary>
public class PeopleTileTests
{
    private static DashPerson Person(string key, string name, int minutesAgo) =>
        new(key, name, "Running late.", T0.AddMinutes(-minutesAgo), Unread: true, Group: false);

    private static readonly DashPeople Four = new(
    [
        Person("c1111111111111111", "Robin", 60),
        Person(LeeKey, "Lee", 2),
        Person("c3333333333333333", "Sam", 600),
        Person("c2222222222222222", "Juniper’s school", 180),
    ]);

    [Fact]
    public void TheNewestNameLeads()
    {
        var face = PeopleTile.Of(Four);

        Assert.Equal(["Lee", "Robin", "Juniper’s school"], face.Names.Select(name => name.Name));
        Assert.Equal([TileRank.Newest, TileRank.Earlier, TileRank.Earliest], face.Names.Select(name => name.Rank));
        Assert.Null(face.Quiet);
    }

    [Fact]
    public void AtMostThreeNamesAndNoCount()
    {
        // A72: no count. The fourth person is simply not on the tile; nothing says there are more.
        var face = PeopleTile.Of(Four);

        Assert.Equal(3, PeopleTile.MostNames);
        Assert.Equal(PeopleTile.MostNames, face.Names.Count);
        Assert.DoesNotContain(face.Names, name => name.Name == "Sam");
        Assert.DoesNotMatch(@"\d", face.Description);
        Assert.DoesNotContain("Sam", face.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNewestNameIsInFull()
    {
        var long60 = new string('a', DashLimits.Name);
        var face = PeopleTile.Of(new DashPeople([Person(LeeKey, long60, 1)]));

        Assert.Equal(long60, Assert.Single(face.Names).Name);
    }

    [Fact]
    public void NobodyWaitingIsTheFaintDashAlone()
    {
        var face = PeopleTile.Of(new DashPeople([]));

        Assert.True(face.Nobody);
        Assert.Empty(face.Names);
        Assert.Null(face.Quiet);
        Assert.Equal(WatchTokens.Light.Quiet, PeopleTile.GlyphAlpha(face));
        Assert.Equal(DashWords.NobodyWaiting, face.Description);
    }

    [Fact]
    public void SomeoneWaitingIsTheDashAtFullGlass()
    {
        var face = PeopleTile.Of(Four);

        Assert.False(face.Nobody);
        Assert.Equal(1, PeopleTile.GlyphAlpha(face));
    }

    [Fact]
    public void APhoneOutOfReachIsSaidNotDrawnAsNobody()
    {
        var face = PeopleTile.Unreached(DashTrouble.NoPhone);

        Assert.False(face.Nobody);
        Assert.Empty(face.Names);
        Assert.Equal(DashWords.For(DashTrouble.NoPhone), face.Quiet);
        Assert.Equal(DashWords.For(DashTrouble.NoPhone), face.Description);
        Assert.Equal(WatchTokens.Light.Quiet, PeopleTile.GlyphAlpha(face));
    }

    [Fact]
    public void TheDescriptionNamesWhoTheTileShows() =>
        Assert.Equal(DashWords.Waiting(["Lee", "Robin", "Juniper’s school"]), PeopleTile.Of(Four).Description);

    [Fact]
    public void TheEarlierNamesFade()
    {
        Assert.Equal(1, PeopleTile.AlphaOf(TileRank.Newest));
        Assert.Equal(WatchTokens.Light.Earlier, PeopleTile.AlphaOf(TileRank.Earlier));
        Assert.Equal(WatchTokens.Light.Earliest, PeopleTile.AlphaOf(TileRank.Earliest));
        Assert.True(PeopleTile.AlphaOf(TileRank.Earlier) > PeopleTile.AlphaOf(TileRank.Earliest));
    }

    [Fact]
    public void TheTileIsAskedAgainWithinTheQuarterHour() => Assert.Equal(TimeSpan.FromMinutes(10), PeopleTile.Freshness);

    [Fact]
    public void TheVersionFollowsWhatIsDrawn()
    {
        var face = PeopleTile.Of(Four);
        var version = PeopleTile.Version(face, 0x7F102030, 384, 384);

        Assert.Equal(version, PeopleTile.Version(PeopleTile.Of(Four), 0x7F102030, 384, 384));
        Assert.NotEqual(version, PeopleTile.Version(PeopleTile.Of(new DashPeople([Person(LeeKey, "Lee", 2)])), 0x7F102030, 384, 384));
        Assert.NotEqual(version, PeopleTile.Version(face, 0x7F102031, 384, 384));
        Assert.NotEqual(version, PeopleTile.Version(face, 0x7F102030, 456, 456));
        Assert.NotEqual(version, PeopleTile.Version(PeopleTile.Unreached(DashTrouble.NoPhone), 0x7F102030, 384, 384));
    }

    [Fact]
    public void TheVersionCarriesNoName()
    {
        var version = PeopleTile.Version(PeopleTile.Of(Four), 0x7F102030, 384, 384);

        Assert.Matches("^t[0-9a-f]{16}$", version);
        Assert.DoesNotContain("Lee", version, StringComparison.Ordinal);
    }
}
