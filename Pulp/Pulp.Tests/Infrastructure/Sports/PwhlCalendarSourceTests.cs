using System.Text.RegularExpressions;
using System.Net;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Sports;
using Pulp.Infrastructure.Catalogue;
using Pulp.Infrastructure.Sports;

namespace Pulp.Tests.Infrastructure.Sports;

/// <summary>
/// The adapter for the one league whose own site offers a calendar meant for subscribing to (D354). The calendars below
/// are invented clubs on invented dates in the shape of the real "Sync to Your Calendar" file (measured live: a
/// vCalendar of vEvents, each a UID, a UTC DTSTART, a LOCATION, a fixed DESCRIPTION and a SUMMARY of "Away @ Home").
/// The hostile entries carry what a calendar must never be trusted to leave out: scores in the SUMMARY and in the
/// DESCRIPTION, which the adapter must refuse or never read (G15).
/// </summary>
public class PwhlCalendarSourceTests
{
    private static readonly DateOnly Today = new(2026, 12, 10);
    private static readonly SportsWindow Window = new(Today, 1, 3);
    private static readonly LeagueId Pwhl = new("pwhl");

    private static string Entry(string uid, string start, string summary, string description = "You have booked some invented games", string location = "Invented Arena | Nowhere") =>
        $"BEGIN:VEVENT\r\nUID:{uid}@example.com\r\nTZID:UTC\r\nDTSTART:{start}\r\nDTEND:{start}\r\nDTSTAMP:20261101T000000\r\nDESCRIPTION:{description}\r\nLOCATION:{location}\r\nSUMMARY:{summary}\r\nSTATUS:CONFIRMED\r\nEND:VEVENT\r\n";

    private static string Calendar(params string[] entries) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Invented//Calendar//EN\r\n" + string.Concat(entries) + "END:VCALENDAR\r\n";

    private static readonly string Standard = Calendar(
        Entry("g1", "20261205T200000Z", "Ridge Foxes @ Harbor Gulls"),
        Entry("g2", "20261210T230000Z", "Harbor Gulls @ Bay Herons"),
        Entry("g3", "20261212T180000Z", "Bay Herons @ Ridge Foxes"),
        Entry("g4", "20270301T180000Z", "Harbor Gulls @ Ridge Foxes"));

    private readonly RoutedHandler handler = new();
    private readonly LookupTally tally = new();
    private DateTimeOffset now = new(2026, 12, 10, 12, 0, 0, TimeSpan.Zero);

    private PwhlCalendarSource Source() => new(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) }, tally, () => now);

    private PwhlCalendarSource Serving(string body)
    {
        handler.Route(".ics", body);
        return Source();
    }

    [Fact]
    public async Task AScheduleIsOneRequestForTheCalendarAndNothingElse()
    {
        var schedule = await Serving(Standard).GetScheduleAsync(Pwhl, Window, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("assets.contentstack.io", request.Uri.Host);
        Assert.EndsWith(".ics", request.Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(1, tally.Count(LookupSource.PwhlCalendar));
        Assert.Equal(2, schedule.Games.Count);
    }

    [Fact]
    public async Task TheRequestIdentifiesPulpHonestly()
    {
        await Serving(Standard).GetScheduleAsync(Pwhl, Window, CancellationToken.None);

        Assert.Contains("Pulp", handler.Requests[0].Headers["User-Agent"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGameIsAwayAtHomeWithItsStartAndTheCalendarsOwnId()
    {
        var schedule = await Serving(Standard).GetScheduleAsync(Pwhl, Window, CancellationToken.None);

        var game = schedule.Games.Single(candidate => candidate.Id.Value == "g2@example.com");
        Assert.Equal("Bay Herons", game.Home.Name);
        Assert.Equal("Harbor Gulls", game.Away.Name);
        Assert.Equal(new DateTimeOffset(2026, 12, 10, 23, 0, 0, TimeSpan.Zero), game.StartsAt);
        Assert.Equal(string.Empty, game.Channel);
        Assert.Equal("pwhl:harbor-gulls", game.Away.Id.Value);
    }

    [Fact]
    public async Task OnlyGamesInTheWindowAreReturnedButEveryClubInTheCalendarIsATeam()
    {
        var schedule = await Serving(Standard).GetScheduleAsync(Pwhl, Window, CancellationToken.None);

        Assert.Equal(["g2@example.com", "g3@example.com"], schedule.Games.Select(game => game.Id.Value));
        Assert.Equal(["Bay Herons", "Harbor Gulls", "Ridge Foxes"], schedule.Teams.Select(team => team.Name).Order());
    }

    [Fact]
    public async Task EveryGameIsTheRegularSeasonBecauseThatIsTheFileAndThereIsNoChampionship()
    {
        var schedule = await Serving(Standard).GetScheduleAsync(Pwhl, Window, CancellationToken.None);

        Assert.All(schedule.Games, game => Assert.Equal(GameStage.RegularSeason, game.Stage));
        Assert.Null(schedule.Championship);
    }

    [Fact]
    public async Task TheLeagueIsOffSeasonUntilItsFirstGameThenInSeasonThenOffSeasonAgain()
    {
        var before = await Serving(Standard).GetScheduleAsync(Pwhl, new SportsWindow(new DateOnly(2026, 10, 16), 1, 3), CancellationToken.None);
        Assert.Equal(SeasonPhase.OffSeason, before.League.Phase);
        Assert.Equal(new DateOnly(2026, 12, 5), before.League.NextDate);
        Assert.Equal(new DateOnly(2027, 3, 1), before.League.SeasonEnds);

        now = now.AddHours(1);
        var during = await Source().GetScheduleAsync(Pwhl, Window, CancellationToken.None);
        Assert.Equal(SeasonPhase.RegularSeason, during.League.Phase);

        now = now.AddHours(1);
        var after = await Source().GetScheduleAsync(Pwhl, new SportsWindow(new DateOnly(2027, 6, 1), 1, 3), CancellationToken.None);
        Assert.Equal(SeasonPhase.OffSeason, after.League.Phase);
        Assert.Null(after.League.NextDate);
    }

    [Fact]
    public async Task AScoreInTheSummaryRefusesTheEntryAndNothingOfItSurvives()
    {
        var hostile = Calendar(
            Entry("ok", "20261210T230000Z", "Harbor Gulls @ Bay Herons"),
            Entry("h1", "20261211T230000Z", "Harbor Gulls 3 @ Bay Herons 2"),
            Entry("h2", "20261211T230000Z", "Harbor Gulls @ Bay Herons 3-2"),
            Entry("h3", "20261211T230000Z", "Harbor Gulls 3-2 Bay Herons"),
            Entry("h4", "20261211T230000Z", "Harbor Gulls @ Bay Herons (W 3-2)"),
            Entry("h5", "20261211T230000Z", "Harbor Gulls @ Bay Herons: 3 to 2"),
            Entry("h6", "20261211T230000Z", "Harbor Gulls @ Bay Herons - OT"),
            Entry("h7", "20261211T230000Z", "Harbor Gulls vs Bay Herons"));

        var schedule = await Serving(hostile).GetScheduleAsync(Pwhl, Window, CancellationToken.None);

        Assert.Equal(["ok@example.com"], schedule.Games.Select(game => game.Id.Value));
        Assert.All(schedule.Teams, team => Assert.DoesNotMatch("[0-9(:]", team.Name));
    }

    [Fact]
    public async Task AScoreInTheDescriptionOrAnyOtherFieldIsNeverReadSoNeverSurfaces()
    {
        var hostile = Calendar(
            Entry("d1", "20261211T230000Z", "Harbor Gulls @ Bay Herons", description: "FINAL Harbor Gulls 7 Bay Herons 2 SO", location: "Arena 7-2 winner Gulls"));

        var schedule = await Serving(hostile).GetScheduleAsync(Pwhl, Window, CancellationToken.None);

        var game = Assert.Single(schedule.Games);
        var everything = string.Join('|', game.Id.Value, game.Name, game.Channel, game.Home.Name, game.Away.Name, game.Home.Id.Value, game.Away.Id.Value);
        Assert.DoesNotContain("7", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("FINAL", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("winner", everything, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AStartNotInUtcIsRefusedRatherThanGuessedAtAndAMissingOneToo()
    {
        var odd = Calendar(
            "BEGIN:VEVENT\r\nUID:z1@example.com\r\nDTSTART;TZID=America/Invented:20261211T180000\r\nSUMMARY:Harbor Gulls @ Bay Herons\r\nEND:VEVENT\r\n",
            "BEGIN:VEVENT\r\nUID:z2@example.com\r\nSUMMARY:Harbor Gulls @ Bay Herons\r\nEND:VEVENT\r\n",
            "BEGIN:VEVENT\r\nDTSTART:20261211T180000Z\r\nSUMMARY:Harbor Gulls @ Bay Herons\r\nEND:VEVENT\r\n",
            Entry("fine", "20261211T180000Z", "Harbor Gulls @ Bay Herons"));

        var schedule = await Serving(odd).GetScheduleAsync(Pwhl, Window, CancellationToken.None);

        Assert.Equal(["fine@example.com"], schedule.Games.Select(game => game.Id.Value));
    }

    [Fact]
    public async Task FoldedLinesEscapesAndAccentsAreReadAsTheCalendarMeantThem()
    {
        var folded = Calendar(
            "BEGIN:VEVENT\r\nUID:f1@example.com\r\nDTSTART:20261211T180000Z\r\nSUMMARY:Montréal Harbor\r\n  Gulls @ O'Neil Bay Herons\r\nEND:VEVENT\r\n");

        var schedule = await Serving(folded).GetScheduleAsync(Pwhl, Window, CancellationToken.None);

        var game = Assert.Single(schedule.Games);
        Assert.Equal("Montréal Harbor Gulls", game.Away.Name);
        Assert.Equal("pwhl:montreal-harbor-gulls", game.Away.Id.Value);
        Assert.Equal("O'Neil Bay Herons", game.Home.Name);
    }

    [Fact]
    public async Task ADuplicatedEntryIsOneGame()
    {
        var twice = Calendar(
            Entry("dup", "20261211T180000Z", "Harbor Gulls @ Bay Herons"),
            Entry("dup", "20261211T180000Z", "Harbor Gulls @ Bay Herons"));

        var schedule = await Serving(twice).GetScheduleAsync(Pwhl, Window, CancellationToken.None);

        Assert.Single(schedule.Games);
    }

    [Fact]
    public async Task TheSameCalendarIsNotAskedForTwiceWithinSixHoursButIsAfter()
    {
        var source = Serving(Standard);

        await source.GetScheduleAsync(Pwhl, new SportsWindow(Today, 0, 0), CancellationToken.None);
        await source.GetScheduleAsync(Pwhl, Window, CancellationToken.None);
        Assert.Single(handler.Requests);

        now = now.AddHours(5);
        await source.GetScheduleAsync(Pwhl, Window, CancellationToken.None);
        Assert.Single(handler.Requests);

        now = now.AddHours(2);
        await source.GetScheduleAsync(Pwhl, Window, CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("Harbor Gulls won @ Bay Herons")]
    [InlineData("Harbor Gulls @ Bay Herons Final")]
    [InlineData("Harbor Gulls beat Bay Herons @ Ridge Foxes")]
    [InlineData("Harbor Gulls @ Bay Herons SHOOTOUT")]
    [InlineData("Harbor Gulls def @ Bay Herons")]
    [InlineData("Harbor Gulls @ Bay Herons OT")]
    [InlineData("Harbor Gulls lost @ Bay Herons")]
    [InlineData("Harbor Gulls tied @ Bay Herons")]
    [InlineData("Harbor Gulls @ Bay Herons Draw")]
    [InlineData("Harbor Gulls wins @ Bay Herons")]
    [InlineData("Harbor Gulls lose @ Bay Herons")]
    [InlineData("Harbor Gulls beats @ Bay Herons")]
    [InlineData("Harbor Gulls so @ Bay Herons")]
    public async Task AResultWordInAClubNameRefusesTheEntryEvenWithNoDigit(string summary)
    {
        var hostile = Calendar(Entry("ok", "20261210T230000Z", "Harbor Gulls @ Bay Herons"), Entry("h1", "20261211T230000Z", summary));

        var schedule = await Serving(hostile).GetScheduleAsync(Pwhl, Window, CancellationToken.None);

        Assert.Equal(["ok@example.com"], schedule.Games.Select(game => game.Id.Value));
        Assert.DoesNotContain(schedule.Teams, team => Regex.IsMatch(team.Name, @"\b(won|wins|beat|beats|def|final|lost|lose|ot|so|shootout|draw|tied)\b", RegexOptions.IgnoreCase));
    }

    [Fact]
    public async Task AResultWordInsideALongerWordIsNotARefusal()
    {
        var calendar = Calendar(Entry("g1", "20261210T230000Z", "Sotheby Finals Club @ Drawbridge Otters"));

        var schedule = await Serving(calendar).GetScheduleAsync(Pwhl, Window, CancellationToken.None);

        Assert.Single(schedule.Games);
    }

    private static string ClubsFile(int clubs) =>
        Calendar(Enumerable.Range(0, clubs).Select(index => Entry($"g{index}", "20261210T230000Z", $"{Letters(index)} Foxes @ {Letters(index + 1)} Foxes")).ToArray());

    // Letters only, since a club name with a digit is already refused: 0 -> "Alpha", 1 -> "Bravo" ... made distinct by a suffix of letters.
    private static string Letters(int index) => "Club" + string.Concat(index.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(digit => (char)('a' + (digit - '0'))));

    [Fact]
    public async Task AFileWithMoreThanSixteenDistinctClubsIsRefusedWholeAndSixteenIsRead()
    {
        // A chain of 16 games names 17 clubs; 15 games name 16.
        var many = await Assert.ThrowsAsync<CatalogueLookupException>(() => Serving(ClubsFile(16)).GetScheduleAsync(Pwhl, Window, CancellationToken.None));
        Assert.Equal(LookupFailure.Malformed, many.Failure);

        handler.Route(".ics", ClubsFile(15));
        now = now.AddDays(1);
        var fine = await Source().GetScheduleAsync(Pwhl, new SportsWindow(new DateOnly(2026, 12, 10), 1, 3), CancellationToken.None);
        Assert.Equal(16, fine.Teams.Count);
    }

    [Fact]
    public async Task ABodyOfInventedClubsWrappingOneRealLookingGameIsRefusedNotTrimmedToIt()
    {
        var entries = Enumerable.Range(0, 20).Select(index => Entry($"x{index}", "20261210T230000Z", $"{Letters(index * 2)} Foxes @ {Letters((index * 2) + 1)} Foxes")).ToList();
        entries.Add(Entry("ok", "20261210T230000Z", "Harbor Gulls @ Bay Herons"));

        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(() => Serving(Calendar(entries.ToArray())).GetScheduleAsync(Pwhl, Window, CancellationToken.None));

        Assert.Equal(LookupFailure.Malformed, thrown.Failure);
    }

    [Fact]
    public async Task ABodyThatIsNotACalendarIsMalformedAndAFailedResponseIsUnavailable()
    {
        var notCalendar = await Assert.ThrowsAsync<CatalogueLookupException>(
            () => Serving("<html>an invented page</html>").GetScheduleAsync(Pwhl, Window, CancellationToken.None));
        Assert.Equal(LookupFailure.Malformed, notCalendar.Failure);
        Assert.Equal(LookupSource.PwhlCalendar, notCalendar.Source);

        var gone = new RoutedHandler().Route(".ics", "nope", HttpStatusCode.NotFound);
        var down = await Assert.ThrowsAsync<CatalogueLookupException>(
            () => new PwhlCalendarSource(new HttpClient(gone), null, () => now).GetScheduleAsync(Pwhl, Window, CancellationToken.None));
        Assert.Equal(LookupFailure.Unavailable, down.Failure);
    }

    [Fact]
    public async Task ACalendarWithNoReadableGameIsMalformedNotAnEmptySeason()
    {
        var thrown = await Assert.ThrowsAsync<CatalogueLookupException>(
            () => Serving(Calendar(Entry("h1", "20261211T180000Z", "Harbor Gulls 3 @ Bay Herons 2"))).GetScheduleAsync(Pwhl, Window, CancellationToken.None));

        Assert.Equal(LookupFailure.Malformed, thrown.Failure);
    }

    [Fact]
    public async Task ACalendarHasNoRankOrderSoStandingsAreEmptyAndNothingIsRequested()
    {
        var order = await Source().GetStandingsOrderAsync(Pwhl, CancellationToken.None);

        Assert.Empty(order);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AnotherLeagueIsRefused()
    {
        var source = Serving(Standard);

        Assert.Equal([Pwhl], source.Leagues);
        await Assert.ThrowsAsync<ArgumentException>(() => source.GetScheduleAsync(new LeagueId("nhl"), Window, CancellationToken.None));
    }
}
