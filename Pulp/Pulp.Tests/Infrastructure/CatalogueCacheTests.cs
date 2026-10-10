using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Ratings;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Tests.Infrastructure;

/// <summary>
/// The on-device copy of fetched titles, which is what lets a screen paint at once and the month's
/// lookups last (D204). A field the cache drops is a field the screen shows blank until the next
/// weekly refresh — an expiry date gone, a deep link gone — with nothing failing.
/// </summary>
public sealed class CatalogueCacheTests : IDisposable
{
    private static readonly DateTimeOffset Fetched = new(2026, 5, 20, 19, 0, 0, TimeSpan.Zero);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "pulp-cache-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TitleOffer Offer(OfferKind kind, decimal? price = null, DateOnly? expires = null) =>
        new(new ServiceId("streamly-example"), "Streamly", true, kind, VideoQuality.Uhd, price, price is null ? null : "USD",
            "https://example.com/watch/1", "https://example.com/video/1", expires, expires is not null);

    private static CatalogueTitle Show() => new()
    {
        Id = new TitleId("orchard-show-example"),
        Kind = TitleKind.Show,
        Name = "The Orchard \"Keepers\" — ünïcode",
        ImdbId = "tt9900042",
        Year = 2021,
        Genres = ["drama", "mystery"],
        Synopsis = "An invented synopsis.",
        PosterUrl = "https://example.com/poster.jpg",
        SeasonCount = 2,
        EpisodeCount = 3,
        Rating = ContentRating.Parse("TV-14"),
        Offers = [Offer(OfferKind.Subscription, expires: new DateOnly(2026, 6, 30)), Offer(OfferKind.Rent, price: 3.99m)],
        Ended = false,
        Seasons =
        [
            new CatalogueSeason(1, "Season 1",
            [
                new CatalogueEpisode(new EpisodeRef(1, 1), "Pilot", "First.", 2021, new DateOnly(2021, 3, 4),
                    new DateTimeOffset(2021, 3, 4, 21, 0, 0, TimeSpan.FromHours(-5)), [Offer(OfferKind.Free)]),
                new CatalogueEpisode(new EpisodeRef(1, 2), "Second", "", null, null, null, []),
            ]),
            new CatalogueSeason(2, "Season 2", [new CatalogueEpisode(new EpisodeRef(2, 1), "Return", "", 2023, null, null, [])]),
        ],
    };

    private static CatalogueTitle Film() => new()
    {
        Id = new TitleId("lantern-film-example"),
        Kind = TitleKind.Film,
        Name = "Lantern",
        RuntimeMinutes = 118,
    };

    [Fact]
    public void ATitleComesBackWithEveryField()
    {
        var cache = new CatalogueCache(directory);
        var show = Show();
        cache.Store(show, Fetched);

        var read = new CatalogueCache(directory).Read(show.Id);

        Assert.Equal(CacheReadStatus.Hit, read.Status);
        var back = read.Entry!.Title;
        Assert.Equal(new CacheStamp(Fetched, false), read.Entry.Stamp);
        Assert.Equal(show.Id, back.Id);
        Assert.Equal(show.Kind, back.Kind);
        Assert.Equal(show.Name, back.Name);
        Assert.Equal(show.ImdbId, back.ImdbId);
        Assert.Equal(show.Year, back.Year);
        Assert.Equal(show.Genres, back.Genres);
        Assert.Equal(show.Synopsis, back.Synopsis);
        Assert.Equal(show.PosterUrl, back.PosterUrl);
        Assert.Null(back.RuntimeMinutes);
        Assert.Equal(show.SeasonCount, back.SeasonCount);
        Assert.Equal(show.EpisodeCount, back.EpisodeCount);
        Assert.Equal(show.Rating, back.Rating);
        Assert.Equal(show.Offers, back.Offers);
        Assert.Equal(show.Ended, back.Ended);
        Assert.Equal(show.Seasons.Count, back.Seasons.Count);
        for (var s = 0; s < show.Seasons.Count; s++)
        {
            Assert.Equal(show.Seasons[s].Number, back.Seasons[s].Number);
            Assert.Equal(show.Seasons[s].Name, back.Seasons[s].Name);
            Assert.Equal(show.Seasons[s].Episodes.Count, back.Seasons[s].Episodes.Count);
            for (var e = 0; e < show.Seasons[s].Episodes.Count; e++)
            {
                var expected = show.Seasons[s].Episodes[e];
                var actual = back.Seasons[s].Episodes[e];
                Assert.Equal(expected with { Offers = [] }, actual with { Offers = [] });
                Assert.Equal(expected.Offers, actual.Offers);
            }
        }
    }

    [Fact]
    public void AFilmWithNothingOptionalRoundTrips()
    {
        var cache = new CatalogueCache(directory);
        cache.Store(Film(), Fetched);

        var back = cache.Read(Film().Id).Entry!.Title;

        Assert.Equal(118, back.RuntimeMinutes);
        Assert.Null(back.Ended);
        Assert.Null(back.ImdbId);
        Assert.False(back.Rating.IsRated);
        Assert.Empty(back.Offers);
    }

    [Fact]
    public void ATitleNeverStoredIsMissing() =>
        Assert.Equal(CacheReadStatus.Missing, new CatalogueCache(directory).Read(new TitleId("never-example")).Status);

    [Fact]
    public void IdsDifferingOnlyInCaseAreOneEntry()
    {
        var cache = new CatalogueCache(directory);
        cache.Store(Film(), Fetched);

        Assert.Equal(CacheReadStatus.Hit, cache.Read(new TitleId("LANTERN-film-example")).Status);
    }

    [Fact]
    public void AChangeFromTheFeedMarksTheCopyStaleButKeepsIt()
    {
        var cache = new CatalogueCache(directory);
        cache.Store(Film(), Fetched);

        cache.Invalidate(Film().Id);
        cache.Invalidate(new TitleId("never-example"));

        var read = cache.Read(Film().Id);
        Assert.Equal(CacheReadStatus.Hit, read.Status);
        Assert.True(read.Entry!.Stamp.Invalidated);
        Assert.Equal("Lantern", read.Entry.Title.Name);

        cache.Store(Film(), Fetched.AddHours(1));
        Assert.False(cache.Read(Film().Id).Entry!.Stamp.Invalidated);
    }

    [Fact]
    public void ADamagedEntryIsReportedSoItCanBeLoggedAndFetchedAgain()
    {
        var cache = new CatalogueCache(directory);
        cache.Store(Film(), Fetched);
        foreach (var file in Directory.GetFiles(directory))
        {
            File.WriteAllText(file, "{\"format\":1,\"tit");
        }

        var read = cache.Read(Film().Id);

        Assert.Equal(CacheReadStatus.Unreadable, read.Status);
        Assert.Null(read.Entry);
        Assert.False(string.IsNullOrWhiteSpace(read.Problem));
    }

    [Fact]
    public void AnEntryFromANewerCacheFormatIsReadAsUnreadableNotGuessedAt()
    {
        var cache = new CatalogueCache(directory);
        cache.Store(Film(), Fetched);
        foreach (var file in Directory.GetFiles(directory))
        {
            File.WriteAllText(file, File.ReadAllText(file).Replace("\"format\":1", "\"format\":99", StringComparison.Ordinal));
        }

        Assert.Equal(CacheReadStatus.Unreadable, cache.Read(Film().Id).Status);
    }

    [Fact]
    public void StoringLeavesNoTemporaryFiles()
    {
        var cache = new CatalogueCache(directory);
        cache.Store(Film(), Fetched);
        cache.Store(Show(), Fetched);

        Assert.Equal(2, Directory.GetFiles(directory).Length);
        Assert.DoesNotContain(Directory.GetFiles(directory), file => file.EndsWith(".tmp", StringComparison.Ordinal));
    }
}
