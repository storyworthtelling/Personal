using Pulp.Domain.Sports;
using Pulp.Infrastructure.Catalogue;
using Pulp.Infrastructure.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Infrastructure.Sports;

/// <summary>
/// The on-device copy of each league's feed, which is what lets the Sports tab paint at once (the same
/// pattern as the catalogue cache). A field the cache drops is a field the screen lacks until the next
/// refresh, with nothing failing — so the round trip is held field by field. It holds rank order and
/// schedules and, like everything else, no result.
/// </summary>
public sealed class SportsCacheTests : IDisposable
{
    private static readonly DateTimeOffset Fetched = new(2026, 1, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "pulp-sports-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AFeedRoundTripsWithEveryField()
    {
        var cache = new SportsCache(directory);
        var feed = CoastalFeed() with { League = CoastalLeague(SeasonPhase.OffSeason) with { NextDate = new DateOnly(2026, 9, 1) } };

        cache.Store(feed, Fetched);
        var read = cache.Read(Coastal);

        Assert.Equal(CacheReadStatus.Hit, read.Status);
        var back = read.Entry!.Feed;
        Assert.Equal(Fetched, read.Entry.FetchedAt);
        Assert.Equal(feed.League, back.League);
        Assert.Equal(feed.Teams, back.Teams);
        Assert.Equal(feed.Schedule, back.Schedule);
        Assert.Equal(feed.Standings, back.Standings);
        Assert.Equal(feed.Highlights, back.Highlights);
        Assert.Equal(feed.Championship!.Id, back.Championship!.Id);
        Assert.Equal(feed.Championship.Name, back.Championship.Name);
        Assert.Equal(feed.Championship.StartsAt, back.Championship.StartsAt);
        Assert.Equal(feed.Championship.Teams, back.Championship.Teams);
        Assert.Equal(feed.Schedule[0].StartsAt.Offset, back.Schedule[0].StartsAt.Offset);
    }

    [Fact]
    public void AFeedWithNoChampionshipOrHighlightsRoundTrips()
    {
        var cache = new SportsCache(directory);
        var feed = CoastalFeed() with { Championship = null, Highlights = [], Standings = [] };

        cache.Store(feed, Fetched);

        var back = cache.Read(Coastal).Entry!.Feed;
        Assert.Null(back.Championship);
        Assert.Empty(back.Highlights);
        Assert.Empty(back.Standings);
    }

    [Fact]
    public void ALeagueNeverStoredIsMissing()
    {
        Assert.Equal(CacheReadStatus.Missing, new SportsCache(directory).Read(Southern).Status);
    }

    [Fact]
    public void ADamagedEntryIsUnreadableWithAReasonAndIsReplacedByTheNextStore()
    {
        var cache = new SportsCache(directory);
        cache.Store(CoastalFeed(), Fetched);
        File.WriteAllText(Directory.GetFiles(directory).Single(), "{ not json");

        var read = cache.Read(Coastal);

        Assert.Equal(CacheReadStatus.Unreadable, read.Status);
        Assert.Contains("cfl-invented", read.Problem);
        cache.Store(CoastalFeed(), Fetched);
        Assert.Equal(CacheReadStatus.Hit, cache.Read(Coastal).Status);
    }

    [Fact]
    public void AnEntryFromANewerFormatIsUnreadableNotMisread()
    {
        var cache = new SportsCache(directory);
        cache.Store(CoastalFeed(), Fetched);
        var file = Directory.GetFiles(directory).Single();
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"format\":1", "\"format\":99"));

        Assert.Equal(CacheReadStatus.Unreadable, cache.Read(Coastal).Status);
    }

    [Fact]
    public void LeaguesAreKeptApartAndComparedWithoutCase()
    {
        var cache = new SportsCache(directory);
        cache.Store(CoastalFeed(), Fetched);

        Assert.Equal(CacheReadStatus.Hit, cache.Read(new LeagueId("CFL-INVENTED")).Status);
        Assert.Equal(CacheReadStatus.Missing, cache.Read(Lakeshore).Status);
    }
}
