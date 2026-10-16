using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Sports;

/// <summary>
/// The words on a Sports row (mockup 07): decided in Domain so the head only paints them. Times are
/// 12-hour in the viewer's own zone (§11) and day labels are relative; nothing here can say how a game
/// went, because nothing here is given that.
/// </summary>
public class SportsScreenTextTests
{
    // The viewer's zone is five hours behind UTC, as the invented clock is: Tuesday 20 Jan 2026, noon.
    internal static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("viewer", TimeSpan.FromHours(-5), "viewer", "viewer");

    private static GameLine Line(DateTimeOffset startsAt, bool recorded, string channel = "Gridline TV · channel 7", GameStage stage = GameStage.Playoffs) =>
        new(new GameId("g-text"), Foxes, Gulls, startsAt, stage, channel, recorded);

    [Fact]
    public void ARecordedGameSaysItIsRecordedAndPlaysFromTheStart()
    {
        var detail = SportsScreenText.GameDetail(Line(Now.AddDays(-2), recorded: true), Now, Zone);

        Assert.Equal("Sunday · recorded · from the start", detail);
    }

    [Fact]
    public void ANextGameNamesItsDayAndItsTwelveHourTime()
    {
        var detail = SportsScreenText.GameDetail(Line(Now.AddDays(5), recorded: false), Now, Zone);

        Assert.Equal("Sunday · 12:00 pm", detail);
    }

    [Theory]
    [InlineData(0, 5, "12:05 am")]
    [InlineData(9, 0, "9:00 am")]
    [InlineData(12, 0, "12:00 pm")]
    [InlineData(13, 30, "1:30 pm")]
    [InlineData(23, 59, "11:59 pm")]
    public void TimeIsTwelveHourWithLowercaseMeridiemAndNoLeadingZero(int hour, int minute, string expected)
    {
        var moment = new DateTimeOffset(2026, 1, 25, hour, minute, 0, TimeSpan.FromHours(-5));

        Assert.Equal(expected, SportsScreenText.Clock12(moment, Zone));
    }

    [Fact]
    public void TheClockIsReadInTheViewersZoneNotTheFeedsOffset()
    {
        var utc = new DateTimeOffset(2026, 1, 25, 23, 0, 0, TimeSpan.Zero);

        Assert.Equal("6:00 pm", SportsScreenText.Clock12(utc, Zone));
    }

    [Theory]
    [InlineData(0, 9, "Today")]
    [InlineData(0, 19, "Tonight")]
    [InlineData(1, 12, "Tomorrow")]
    [InlineData(3, 12, "Friday")]
    [InlineData(6, 12, "Monday")]
    [InlineData(7, 12, "Jan 27")]
    [InlineData(-1, 12, "Yesterday")]
    [InlineData(-2, 12, "Sunday")]
    [InlineData(-9, 12, "Jan 11")]
    public void DaysAreRelativeToTheViewersToday(int daysFromNow, int hour, string expected)
    {
        var at = new DateTimeOffset(2026, 1, 20, hour, 0, 0, TimeSpan.FromHours(-5)).AddDays(daysFromNow);

        Assert.Equal(expected, SportsScreenText.DayLabel(at, Now, Zone));
    }

    [Fact]
    public void TheChannelLineNamesWhereItIsOnAndIsAbsentWhenThereIsNoChannel()
    {
        Assert.Equal("On Gridline TV · channel 7", SportsScreenText.ChannelLine(" Gridline TV · channel 7 "));
        Assert.Null(SportsScreenText.ChannelLine("  "));
        Assert.Null(SportsScreenText.ChannelLine(null));
    }

    [Theory]
    [InlineData(GameStage.Preseason, "Preseason")]
    [InlineData(GameStage.RegularSeason, null)]
    [InlineData(GameStage.Playoffs, "Playoffs")]
    [InlineData(GameStage.Championship, "Final")]
    public void TheStageChipNamesOnlyWhatIsNotOrdinary(GameStage stage, string? chip) =>
        Assert.Equal(chip, SportsScreenText.StageChip(stage));

    [Fact]
    public void TheChampionshipLineIsItsKindAndItsDate()
    {
        Assert.Equal("Championship · Sun Feb 8", SportsScreenText.ChampionshipDetail(Now.AddDays(19), Zone));
    }

    private static readonly DateOnly Today = new(2026, 1, 20);

    private static LeagueLine LeagueLine(SeasonPhase phase, DateOnly? next) =>
        new(Coastal, "A League", "badge://x", phase, next);

    [Theory]
    [InlineData(SeasonPhase.Preseason, 2026, 1, 23, "Preseason · next Fri")]
    [InlineData(SeasonPhase.Preseason, 2026, 1, 30, "Preseason · Jan 30")]
    [InlineData(SeasonPhase.OffSeason, 2026, 11, 8, "Back Nov 8")]
    [InlineData(SeasonPhase.OffSeason, 2027, 1, 9, "Back Jan 9, 2027")]
    public void AFoldedLeagueStatesItsStateWithItsNextDate(SeasonPhase phase, int year, int month, int day, string expected) =>
        Assert.Equal(expected, SportsScreenText.LeagueState(LeagueLine(phase, new DateOnly(year, month, day)), Today));

    [Theory]
    [InlineData(SeasonPhase.Preseason, "Preseason")]
    [InlineData(SeasonPhase.OffSeason, "Off season")]
    [InlineData(SeasonPhase.RegularSeason, "In season")]
    [InlineData(SeasonPhase.Postseason, "Playoffs")]
    public void ALeagueWithNoDateStillSaysWhereItIs(SeasonPhase phase, string expected) =>
        Assert.Equal(expected, SportsScreenText.LeagueState(LeagueLine(phase, null), Today));

    [Theory]
    [InlineData("Coastal Football League", 3, "CFL")]
    [InlineData("Lakeshore Women's Hockey", 3, "LWH")]
    [InlineData("Harbor Gulls", 2, "HG")]
    [InlineData("Gulls", 2, "GU")]
    [InlineData("Bay Herons of the North", 3, "BHN")]
    [InlineData("  ", 2, "")]
    public void AMonogramStandsInUntilTheBadgeArrives(string name, int letters, string expected) =>
        Assert.Equal(expected, SportsScreenText.Monogram(name, letters));

    [Fact]
    public void PlayNamesTheChannelAndAdmitsPulpCannotOpenTheGameYet()
    {
        Assert.Equal(
            "On Gridline TV · channel 7. Pulp can't open this game for you yet.",
            SportsScreenText.PlayNote("On Gridline TV · channel 7"));
        Assert.Equal("Pulp can't open this game for you yet.", SportsScreenText.PlayNote(null));
    }

    [Fact]
    public void PlayWhileTheHouseholdHasNotLoadedSaysSoAndNeverTellsAnyoneToTickAService()
    {
        Assert.Equal("Loading your services…", SportsScreenText.ServicesLoading);
        Assert.DoesNotContain("Tick", SportsScreenText.ServicesLoading, StringComparison.Ordinal);
    }
}
