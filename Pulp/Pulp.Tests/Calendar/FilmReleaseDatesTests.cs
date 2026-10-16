using Pulp.Domain.Calendar;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.Services;

namespace Pulp.Tests.Calendar;

/// <summary>
/// A film's release date on the calendar (D302): taken only from the availability source's upcoming-title
/// changes, never invented, never from the catalogue's release year. Invented titles throughout.
/// </summary>
public class FilmReleaseDatesTests
{
    private static readonly ViewerId Sam = new("sam");
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Invented/Harbor", TimeSpan.FromHours(-5), "Invented Harbor", "Invented Harbor");
    private static readonly ServiceId Northstar = new("northstar-plus");
    private static readonly DateTimeOffset Arrival = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

    private static CatalogueChange Change(
        string title, ChangeKind kind = ChangeKind.Upcoming, ChangeItem item = ChangeItem.Title,
        TitleKind titleKind = TitleKind.Film, OfferKind offer = OfferKind.Subscription, DateTimeOffset? at = null, bool dated = true) =>
        new(kind, item, new TitleId(title), titleKind, Northstar, offer, null, null, dated ? at ?? Arrival : null, null);

    private static CatalogueTitle Film(string id) => new() { Id = new TitleId(id), Kind = TitleKind.Film, Name = id, Year = 2026 };

    [Fact]
    public void AnUpcomingFilmWithADateBecomesADayInTheHouseholdsZone()
    {
        var dates = FilmReleaseDates.FromChanges([Change("high-pass")], Zone);

        var date = Assert.Single(dates);
        Assert.Equal(new TitleId("high-pass"), date.Title);
        Assert.Equal(Northstar, date.Service);
        Assert.Equal(new DateOnly(2026, 10, 14), date.Date);
    }

    [Fact]
    public void NothingIsInventedForAChangeWithNoAnnouncedDate()
    {
        Assert.Empty(FilmReleaseDates.FromChanges([Change("high-pass", dated: false)], Zone));
    }

    [Fact]
    public void OnlyUpcomingWholeFilmsOnAServiceTheHouseholdPaysFor()
    {
        var dates = FilmReleaseDates.FromChanges(
        [
            Change("expiring-film", kind: ChangeKind.Expiring),
            Change("a-season", item: ChangeItem.Season),
            Change("a-show", titleKind: TitleKind.Show),
            Change("a-rental", offer: OfferKind.Rent),
            Change("a-purchase", offer: OfferKind.Buy),
            Change("kept"),
        ], Zone);

        Assert.Equal([new TitleId("kept")], dates.Select(date => date.Title));
    }

    [Fact]
    public void TheEarliestDateWinsWhenAFilmComesToTwoServices()
    {
        var later = Change("high-pass", at: Arrival.AddDays(10)) with { Service = new ServiceId("streamly") };

        var dates = FilmReleaseDates.FromChanges([later, Change("high-pass")], Zone);

        Assert.Equal(new DateOnly(2026, 10, 14), Assert.Single(dates).Date);
        Assert.Equal(Northstar, dates[0].Service);
    }

    [Fact]
    public void AFilmOnAPlaylistWithAKnownDateReachesTheCalendarWithItsPlaylist()
    {
        var film = Film("high-pass");
        var shelf = new HouseholdPlaylist(
            new PlaylistId("p-1"), Sam, false,
            Pulp.Domain.Library.Playlist.Named("Rainy Sundays").Add(PlaylistEntry.ForTitle(film.Id, TitleKind.Film)));
        var reads = new CalendarScreenReads(
            new Dictionary<ViewerId, IReadOnlyList<TitleId>>(), [shelf], new Dictionary<TitleId, CatalogueTitle> { [film.Id] = film },
            _ => null, Zone,
            FilmDates: FilmReleaseDates.FromChanges([Change("high-pass")], Zone));

        var release = Assert.Single(CalendarScreenSources.Build(reads, []).Releases);

        Assert.Equal(TitleKind.Film, release.Kind);
        Assert.Equal(new DateOnly(2026, 10, 14), release.Date);
        Assert.Equal(Northstar, release.Service);
        Assert.Equal("high-pass", release.Name);
        Assert.Equal(new PlaylistShelf("Rainy Sundays", Sam, false), Assert.Single(release.Playlists));
    }

    [Fact]
    public void AFilmNotOnAPlaylistOrWithoutADateIsNotOnTheCalendar()
    {
        var film = Film("high-pass");
        var other = Film("not-listed");
        var shelf = new HouseholdPlaylist(
            new PlaylistId("p-1"), Sam, false,
            Pulp.Domain.Library.Playlist.Named("Rainy Sundays").Add(PlaylistEntry.ForTitle(film.Id, TitleKind.Film)));
        var titles = new Dictionary<TitleId, CatalogueTitle> { [film.Id] = film, [other.Id] = other };

        var noDates = new CalendarScreenReads(new Dictionary<ViewerId, IReadOnlyList<TitleId>>(), [shelf], titles, _ => null, Zone);
        var otherDate = noDates with { FilmDates = FilmReleaseDates.FromChanges([Change("not-listed")], Zone) };

        Assert.Empty(CalendarScreenSources.Build(noDates, []).Releases);
        Assert.Empty(CalendarScreenSources.Build(otherDate, []).Releases);
    }

    private static ChangesPage Page(string? next, params CatalogueChange[] changes) =>
        new(changes, new Dictionary<TitleId, CatalogueTitle>(), next);

    [Fact]
    public async Task TheSourceReadsUpcomingTitleChangesPageByPageAndStopsAtTheCap()
    {
        var calls = new List<(ChangeKind Kind, ChangeItem Item, string? Cursor)>();
        var source = new ChangesFilmReleaseSource(
            [new StreamingService(Northstar, "Northstar+", ["com.example.northstar"], "northstar", [])],
            (_, kind, item, cursor, _) =>
            {
                calls.Add((kind, item, cursor));
                return Task.FromResult(Page("more", Change("high-pass" + calls.Count)));
            },
            Zone,
            maxPages: 2);

        var dates = await source.UpcomingAsync(CancellationToken.None);

        Assert.Equal(2, dates.Count);
        Assert.All(calls, call => Assert.Equal((ChangeKind.Upcoming, ChangeItem.Title), (call.Kind, call.Item)));
        Assert.Equal([null, "more"], calls.Select(call => call.Cursor));
    }

    [Fact]
    public async Task ASourceWithNoServicesAsksNothing()
    {
        var asked = false;
        var source = new ChangesFilmReleaseSource([], (_, _, _, _, _) => { asked = true; return Task.FromResult(Page(null)); }, Zone, 2);

        Assert.Empty(await source.UpcomingAsync(CancellationToken.None));
        Assert.False(asked);
    }

    [Fact]
    public async Task TheCacheAsksOncePerLifetimeAndAgainAfterIt()
    {
        var now = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        var inner = new CountingSource();
        var cached = new CachedFilmReleaseSource(inner, () => now, TimeSpan.FromHours(12));

        await cached.UpcomingAsync(CancellationToken.None);
        now = now.AddHours(11);
        await cached.UpcomingAsync(CancellationToken.None);
        Assert.Equal(1, inner.Calls);

        now = now.AddHours(2);
        await cached.UpcomingAsync(CancellationToken.None);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task ConcurrentCallersShareOneFetchInFlight()
    {
        var now = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        var inner = new GatedSource();
        var cached = new CachedFilmReleaseSource(inner, () => now, TimeSpan.FromHours(12));

        var callers = Enumerable.Range(0, 6).Select(_ => cached.UpcomingAsync(CancellationToken.None)).ToList();
        await inner.Started.Task;
        inner.Release.SetResult();
        await Task.WhenAll(callers);

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task OneCallersCancellationDoesNotCancelTheSharedFetchForTheOthers()
    {
        var now = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        var inner = new GatedSource();
        var cached = new CachedFilmReleaseSource(inner, () => now, TimeSpan.FromHours(12));
        using var quitting = new CancellationTokenSource();

        var quits = cached.UpcomingAsync(quitting.Token);
        var stays = cached.UpcomingAsync(CancellationToken.None);
        await inner.Started.Task;
        await quitting.CancelAsync();
        inner.Release.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => quits);
        Assert.Empty(await stays);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task AChangeInTheTickedServicesAsksAgainWithinTheLifetime()
    {
        var now = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        var inner = new CountingSource();
        var ticked = "peacock";
        var cached = new CachedFilmReleaseSource(inner, () => now, TimeSpan.FromHours(12), _ => Task.FromResult(ticked));

        await cached.UpcomingAsync(CancellationToken.None);
        await cached.UpcomingAsync(CancellationToken.None);
        Assert.Equal(1, inner.Calls);

        ticked = "peacock,hulu";
        await cached.UpcomingAsync(CancellationToken.None);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task AFailedFetchIsNotKeptAndTheNextCallerTriesAgain()
    {
        var now = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        var inner = new CountingSource { FailFirst = true };
        var cached = new CachedFilmReleaseSource(inner, () => now, TimeSpan.FromHours(12));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cached.UpcomingAsync(CancellationToken.None));
        await cached.UpcomingAsync(CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }

    private sealed class GatedSource : IFilmReleaseSource
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<FilmReleaseDate>> UpcomingAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            Started.TrySetResult();
            await Release.Task;
            return [];
        }
    }

    private sealed class CountingSource : IFilmReleaseSource
    {
        public int Calls { get; private set; }

        public bool FailFirst { get; init; }

        public Task<IReadOnlyList<FilmReleaseDate>> UpcomingAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (FailFirst && Calls == 1)
            {
                throw new InvalidOperationException("an invented feed fault");
            }

            return Task.FromResult<IReadOnlyList<FilmReleaseDate>>([]);
        }
    }
}
