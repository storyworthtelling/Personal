using Pulp.Domain.Calendar;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.Sports;

namespace Pulp.Tests.Calendar;

/// <summary>
/// The review's four findings on the calendar follow-ups (D358-D361): a film on a playlist is looked up so its
/// known date reaches the page and the household write; one address Google refuses does not stop the night's
/// events; a "Just me" plan stays off the shared calendar; a plan beyond the window is saved and says so.
/// Invented titles, people and addresses throughout.
/// </summary>
public class CalendarReviewFixesTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);
    private static readonly ViewerId Sam = new("sam");
    private static readonly ViewerId Alex = new("alex");
    private static readonly ServiceId Streamly = new("streamly");
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;

    private static HouseholdPlaylist Shelf(string id, ViewerId owner, params PlaylistEntry[] entries) =>
        new(new PlaylistId(id), owner, false,
            entries.Aggregate(Playlist.Named("Rainy Sundays"), (list, entry) => list.Add(entry)));

    private static CatalogueTitle Film(string id) => new() { Id = new TitleId(id), Kind = TitleKind.Film, Name = id };

    [Fact]
    public void AFilmWithAKnownDateOnAPlaylistIsLookedUpAndOtherFilmsAndShowsAreNotLookedUpNeedlessly()
    {
        var shelf = Shelf("p-1", Sam,
            PlaylistEntry.ForTitle(new TitleId("high-pass"), TitleKind.Film),
            PlaylistEntry.ForTitle(new TitleId("undated-film"), TitleKind.Film),
            PlaylistEntry.ForTitle(new TitleId("tidewater"), TitleKind.Show));
        var dates = new[] { new FilmReleaseDate(new TitleId("high-pass"), Streamly, Today.AddDays(3)) };

        var wanted = CalendarTitleWants.Of([new TitleId("saltmarsh")], [shelf], dates);

        Assert.Equivalent(
            new[] { new TitleId("saltmarsh"), new TitleId("tidewater"), new TitleId("high-pass") }, wanted);
    }

    [Fact]
    public async Task AFilmWithAKnownDateReachesThePageRowsAndTheHouseholdWrite()
    {
        var shelf = Shelf("p-1", Sam, PlaylistEntry.ForTitle(new TitleId("high-pass"), TitleKind.Film));
        var dates = new[] { new FilmReleaseDate(new TitleId("high-pass"), Streamly, Today.AddDays(3)) };

        // The lookup the head does: exactly the wanted titles, nothing else.
        var titles = CalendarTitleWants.Of([], [shelf], dates).ToDictionary(id => id, id => Film(id.Value));
        var reads = new CalendarScreenReads(
            new Dictionary<ViewerId, IReadOnlyList<TitleId>>(), [shelf], titles, _ => null, Zone, FilmDates: dates);
        var sources = CalendarScreenSources.Build(reads, []);

        var page = CalendarItems.ForViewer(sources, Sam, Today, Zone, new HashSet<LeagueId>());
        Assert.Equal(["release:high-pass"], page.Select(item => item.Key));

        var held = new Dictionary<ViewerId, IReadOnlySet<LeagueId>> { [Sam] = new HashSet<LeagueId>() };
        var calendar = new FakePulpCalendar();
        await new CalendarWriter(calendar).SyncAsync(
            "cal-1", CalendarItems.ForHousehold(sources, Today, Zone, held), _ => "Sam", Today, Zone, CancellationToken.None);
        Assert.Equal(["insert:release:high-pass"], calendar.Calls);
    }

    private sealed class FakeFeed : ICalendarFeed
    {
        public required CalendarFeedSnapshot Snapshot { get; init; }

        public List<string> NotedFailures { get; } = [];

        public int Notes { get; private set; }

        public Task<CalendarFeedSnapshot> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot);

        public Task SaveCalendarIdAsync(string calendarId, CancellationToken cancellationToken) => Task.CompletedTask;

        public void NoteShareFailures(IReadOnlyList<CalendarShareFailure> failures)
        {
            Notes++;
            NotedFailures.AddRange(failures.Select(failure => failure.Account));
        }
    }

    private sealed class RefusingCalendar : IPulpCalendar
    {
        private readonly FakePulpCalendar inner = new();

        public List<string> Calls => inner.Calls;

        public Task<string> CreateAsync(string title, TimeZoneInfo zone, CancellationToken cancellationToken) =>
            inner.CreateAsync(title, zone, cancellationToken);

        public Task<IReadOnlyList<string>> SharedWithAsync(string calendarId, CancellationToken cancellationToken) =>
            inner.SharedWithAsync(calendarId, cancellationToken);

        public Task ShareAsReaderAsync(string calendarId, string account, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The invented calendar refused " + account + ".");

        public Task<IReadOnlyList<ExistingEvent>> ListAsync(string calendarId, DateOnly from, DateOnly to, TimeZoneInfo zone, CancellationToken cancellationToken) =>
            inner.ListAsync(calendarId, from, to, zone, cancellationToken);

        public Task InsertAsync(string calendarId, CalendarEventDraft draft, TimeZoneInfo zone, CancellationToken cancellationToken) =>
            inner.InsertAsync(calendarId, draft, zone, cancellationToken);

        public Task PatchAsync(string calendarId, string eventId, CalendarEventDraft draft, TimeZoneInfo zone, CancellationToken cancellationToken) =>
            inner.PatchAsync(calendarId, eventId, draft, zone, cancellationToken);

        public Task DeleteAsync(string calendarId, string eventId, CancellationToken cancellationToken) =>
            inner.DeleteAsync(calendarId, eventId, cancellationToken);
    }

    private static CalendarFeedSnapshot OwnerSnapshot() => new(
        new CalendarSources(
            [new EpisodeRelease(new TitleId("harbor-lights"), "Harbor Lights", 1, 4, Today.AddDays(2), null, new ServiceId("northstar-plus"), [Sam])],
            [], [], []),
        new Dictionary<ViewerId, IReadOnlySet<LeagueId>> { [Sam] = new HashSet<LeagueId>() },
        _ => "Sam",
        HouseholdFileRole.Owner,
        "cal-1",
        "owner@example.com",
        ["owner@example.com", "not-an-address"],
        Zone);

    [Fact]
    public async Task AShareGoogleRefusesDoesNotStopTheNightsEventsAndIsReportedByAddress()
    {
        var feed = new FakeFeed { Snapshot = OwnerSnapshot() };
        var calendar = new RefusingCalendar();

        var outcome = await CalendarNightly.RunAsync(feed, calendar, Today, CancellationToken.None);

        Assert.Equal(["insert:episode:harbor-lights:1:4"], calendar.Calls);
        Assert.Equal(1, outcome.Result!.Created);
        Assert.Equal(["not-an-address"], outcome.ShareFailures.Select(failure => failure.Account));
        Assert.IsType<InvalidOperationException>(outcome.ShareFailures[0].Failure);
        Assert.Equal(["not-an-address"], feed.NotedFailures);
    }

    [Fact]
    public async Task ACleanNightTellsTheFeedSoAnOldProblemSentenceClears()
    {
        var feed = new FakeFeed { Snapshot = OwnerSnapshot() with { AdultAccounts = ["owner@example.com"] } };

        await CalendarNightly.RunAsync(feed, new FakePulpCalendar(), Today, CancellationToken.None);

        Assert.Equal(1, feed.Notes);
        Assert.Empty(feed.NotedFailures);
    }

    [Fact]
    public async Task ACancelledShareStillCancels()
    {
        using var source = new CancellationTokenSource();
        var calendar = new FakePulpCalendar { OnShare = source.Cancel };
        var failing = new CancelOnShare(calendar, source);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CalendarHousehold.EnsureAsync(
            failing, HouseholdFileRole.Owner, "cal-1", "owner@example.com", ["owner@example.com", "other@example.com"],
            _ => Task.CompletedTask, Zone, source.Token, []));
    }

    private sealed class CancelOnShare(FakePulpCalendar inner, CancellationTokenSource source) : IPulpCalendar
    {
        public Task<string> CreateAsync(string title, TimeZoneInfo zone, CancellationToken cancellationToken) => inner.CreateAsync(title, zone, cancellationToken);

        public Task<IReadOnlyList<string>> SharedWithAsync(string calendarId, CancellationToken cancellationToken) => inner.SharedWithAsync(calendarId, cancellationToken);

        public Task ShareAsReaderAsync(string calendarId, string account, CancellationToken cancellationToken)
        {
            source.Cancel();
            throw new OperationCanceledException(source.Token);
        }

        public Task<IReadOnlyList<ExistingEvent>> ListAsync(string calendarId, DateOnly from, DateOnly to, TimeZoneInfo zone, CancellationToken cancellationToken) => inner.ListAsync(calendarId, from, to, zone, cancellationToken);

        public Task InsertAsync(string calendarId, CalendarEventDraft draft, TimeZoneInfo zone, CancellationToken cancellationToken) => inner.InsertAsync(calendarId, draft, zone, cancellationToken);

        public Task PatchAsync(string calendarId, string eventId, CalendarEventDraft draft, TimeZoneInfo zone, CancellationToken cancellationToken) => inner.PatchAsync(calendarId, eventId, draft, zone, cancellationToken);

        public Task DeleteAsync(string calendarId, string eventId, CancellationToken cancellationToken) => inner.DeleteAsync(calendarId, eventId, cancellationToken);
    }

    [Theory]
    [InlineData(new[] { "a@example.com" }, "Couldn't share with a@example.com.")]
    [InlineData(new[] { "a@example.com", "b@example.com" }, "Couldn't share with a@example.com or b@example.com.")]
    public void TheStatusNamesWhoTheCalendarCouldNotBeSharedWith(string[] accounts, string sentence)
    {
        Assert.Equal(sentence, CalendarScreenConnection.ShareProblem(accounts));
        Assert.EndsWith(" " + sentence, CalendarScreenConnection.Sentence(HouseholdFileRole.Owner, true, accounts), StringComparison.Ordinal);
    }

    [Fact]
    public void NoProblemMeansTheStatusIsUnchanged()
    {
        Assert.Equal(
            CalendarScreenConnection.Sentence(HouseholdFileRole.Owner, true),
            CalendarScreenConnection.Sentence(HouseholdFileRole.Owner, true, []));
        Assert.Equal(CalendarScreenConnection.NotConnected, CalendarScreenConnection.Sentence(HouseholdFileRole.Owner, false, ["a@example.com"]));
    }

    private static PlannedViewing Plan(string id, params ViewerId[] viewers) =>
        new(id, null, id, Today.AddDays(1), null, viewers);

    [Fact]
    public void AJustMePlanStaysOffTheHouseholdCalendarButOnTheOwnersPage()
    {
        var sources = new CalendarSources([], [], [], [Plan("dentist", Sam), Plan("dinner", Sam, Alex)]);
        var held = new Dictionary<ViewerId, IReadOnlySet<LeagueId>>
        {
            [Sam] = new HashSet<LeagueId>(), [Alex] = new HashSet<LeagueId>(),
        };

        var household = CalendarItems.ForHousehold(sources, Today, Zone, held);
        var page = CalendarItems.ForViewer(sources, Sam, Today, Zone, new HashSet<LeagueId>());

        Assert.Equal(["plan:dinner"], household.Select(item => item.Key));
        Assert.Equal(["plan:dentist", "plan:dinner"], page.Select(item => item.Key).Order());
    }

    [Fact]
    public void InAOneAdultHouseholdTheirOwnPlanIsSharedWithEveryone()
    {
        var sources = new CalendarSources([], [], [], [Plan("dentist", Sam)]);
        var held = new Dictionary<ViewerId, IReadOnlySet<LeagueId>> { [Sam] = new HashSet<LeagueId>() };

        Assert.Equal(["plan:dentist"], CalendarItems.ForHousehold(sources, Today, Zone, held).Select(item => item.Key));
    }

    [Fact]
    public void APlanBeyondTheWindowSaysItWasSavedAndWhenItWillShow()
    {
        var far = Today.AddDays(CalendarItems.DaysAhead + 3);
        var sentence = CalendarPlans.SavedNote(far, Today);

        Assert.Equal("Saved for Oct 26. It will show once it is within two weeks.", sentence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(13)]
    public void APlanInsideTheWindowNeedsNoNote(int daysAhead) =>
        Assert.Null(CalendarPlans.SavedNote(Today.AddDays(daysAhead), Today));

    [Fact]
    public void TheFirstDayOutsideTheWindowGetsTheNoteAndIsNamedByItsDate()
    {
        Assert.Equal(
            "Saved for Oct 23. It will show once it is within two weeks.",
            CalendarPlans.SavedNote(Today.AddDays(CalendarItems.DaysAhead), Today));
    }

    [Fact]
    public void ThePlanIsStillStoredBecauseCheckAcceptsAFarDate() =>
        Assert.Null(CalendarPlans.Check("Birthday dinner", Today.AddDays(60), Today));

    [Fact]
    public void TheHeadWiresTheLookupTheNoteAndTheStatus()
    {
        var root = PulpSource.Root();
        var reads = File.ReadAllText(Path.Combine(root, "Pulp.Android/App/CalendarReads.cs"));
        var area = File.ReadAllText(Path.Combine(root, "Pulp.Android/Shell/CalendarArea.cs"));
        var connection = File.ReadAllText(Path.Combine(root, "Pulp.Android/Calendar/CalendarConnection.cs"));
        var feed = File.ReadAllText(Path.Combine(root, "Pulp.Android/App/PulpCalendarFeed.cs"));

        Assert.Contains("CalendarTitleWants.Of(", reads, StringComparison.Ordinal);
        Assert.Contains("CalendarPlans.SavedNote(", area, StringComparison.Ordinal);
        Assert.Contains("CalendarScreenConnection.Sentence(role, Connected, ", connection, StringComparison.Ordinal);
        Assert.Contains("NoteShareFailures", feed, StringComparison.Ordinal);
    }
}
