using Pulp.Domain.Calendar;
using Pulp.Domain.Core;
using Pulp.Domain.Sports;

namespace Pulp.Tests.Calendar;

/// <summary>
/// The Calendar page's lines as mockup 8 draws them (D146, D148, D244): a title line, a quiet kind-and-
/// service line, whose it is at the end, a mono time or "All day", grouped by day. Invented household:
/// Sam (this phone) and Alex.
/// </summary>
public class CalendarScreenRowsTests
{
    private static readonly ViewerId Sam = new("sam");
    private static readonly ViewerId Alex = new("alex");
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 14, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Invented/Harbor", TimeSpan.FromHours(-5), "Invented Harbor", "Invented Harbor");
    private static readonly DateOnly Today = new(2026, 10, 7);
    private static readonly ServiceId Northstar = new("northstar-plus");
    private static readonly ServiceId Streamly = new("streamly");

    private static readonly CalendarScreenContext Context = new(
        Sam,
        viewer => viewer == Sam ? "Sam" : viewer == Alex ? "Alex" : viewer.Value,
        service => service == Northstar ? "Northstar+" : service == Streamly ? "Streamly" : null,
        title => title.Value switch
        {
            "the-majestic" or "high-pass" => TitleKind.Film,
            _ => null,
        });

    private static CalendarItem Plan(DateOnly day, TimeOnly? time, params ViewerId[] whose) => new(
        "plan:1", CalendarItemKind.Plan, new CalendarWhen(day, time), "The Majestic", null, Streamly,
        new TitleId("the-majestic"), null, whose, []);

    private static CalendarItem Episode(DateOnly day) => new(
        "episode:saltmarsh:1:4", CalendarItemKind.NewEpisode, CalendarWhen.AllDay(day), "Saltmarsh", "S1 E4", Northstar,
        new TitleId("saltmarsh"), null, [Sam], []);

    private static CalendarItem Release(DateOnly day) => new(
        "release:high-pass", CalendarItemKind.Release, CalendarWhen.AllDay(day), "High Pass", null, Northstar,
        new TitleId("high-pass"), null, [Sam], ["Rainy Sundays"]);

    private static CalendarItem Game(DateOnly day) => new(
        "game:g-1", CalendarItemKind.Game, new CalendarWhen(day, new TimeOnly(19, 30)), "Harbor Gulls at Ridge Foxes",
        "Gridline TV", null, null, new LeagueId("cfl-invented"), [Sam, Alex], []);

    [Fact]
    public void MockupEightsThreeDaysReadAsDrawn()
    {
        var items = new[] { Plan(Today, new TimeOnly(20, 0), Sam, Alex), Episode(Today.AddDays(2)), Release(Today.AddDays(7)) };

        var page = CalendarScreen.Build(items, Context, Now, Zone);

        Assert.Equal(["Tonight", "Friday", "Oct 14"], page.Days.Select(day => day.Label));
        var plan = page.Days[0].Rows.Single();
        Assert.Equal("8:00 pm", plan.TimeText);
        Assert.Equal("Plan · The Majestic", plan.Title);
        Assert.Equal("Film · Streamly", plan.Detail);
        Assert.Equal("Sam, Alex", plan.Whose);

        var episode = page.Days[1].Rows.Single();
        Assert.Equal("All day", episode.TimeText);
        Assert.Equal("Saltmarsh · S1 E4", episode.Title);
        Assert.Equal("New episode · Northstar+", episode.Detail);
        Assert.Equal("Sam", episode.Whose);

        var release = page.Days[2].Rows.Single();
        Assert.Equal("High Pass", release.Title);
        Assert.Equal("Film release · Northstar+", release.Detail);
        Assert.Equal("Rainy Sundays", release.Whose);
    }

    [Theory]
    [InlineData(0, 5, "12:05 am")]
    [InlineData(9, 0, "9:00 am")]
    [InlineData(12, 0, "12:00 pm")]
    [InlineData(13, 30, "1:30 pm")]
    [InlineData(23, 59, "11:59 pm")]
    public void TimesAreTwelveHour(int hour, int minute, string expected)
    {
        var page = CalendarScreen.Build([Plan(Today, new TimeOnly(hour, minute), Sam)], Context, Now, Zone);
        Assert.Equal(expected, page.Days.Single().Rows.Single().TimeText);
    }

    [Fact]
    public void ThisViewerIsNamedFirst()
    {
        var item = Plan(Today, null, Alex, Sam);
        var page = CalendarScreen.Build([item], Context, Now, Zone);
        Assert.Equal("Sam, Alex", page.Days.Single().Rows.Single().Whose);
    }

    [Fact]
    public void AnEpisodeOrReleaseOpensItsTitlePage()
    {
        var page = CalendarScreen.Build([Episode(Today), Release(Today)], Context, Now, Zone);
        var rows = page.Days.Single().Rows;
        Assert.Equal([new TitleId("high-pass"), new TitleId("saltmarsh")], rows.Select(row => row.OpenTitle!.Value));
        Assert.All(rows, row => Assert.Null(row.OpenLeague));
    }

    [Fact]
    public void AGameOpensItsLeagueOnSports()
    {
        var page = CalendarScreen.Build([Game(Today)], Context, Now, Zone);
        var row = page.Days.Single().Rows.Single();
        Assert.Equal(new LeagueId("cfl-invented"), row.OpenLeague);
        Assert.Null(row.OpenTitle);
        Assert.Equal("Game · Gridline TV", row.Detail);
        Assert.Equal("7:30 pm", row.TimeText);
    }

    [Fact]
    public void AServiceTheCatalogueDoesNotNameIsLeftOutRatherThanShownAsAnId()
    {
        var item = Episode(Today) with { Service = new ServiceId("some-unlisted-service") };
        var page = CalendarScreen.Build([item], Context, Now, Zone);
        Assert.Equal("New episode", page.Days.Single().Rows.Single().Detail);
    }

    [Fact]
    public void NoItemsIsAnEmptyPage()
    {
        var page = CalendarScreen.Build([], Context, Now, Zone);
        Assert.True(page.IsEmpty);
        Assert.Empty(page.Days);
    }

    [Fact]
    public void ThereIsNoBottomBarAndNoPlanMakingOnThePage()
    {
        // D246/D305: no bottom bar; the page model carries no action beyond opening a row. Adding a plan is the area's own form.
        var members = typeof(CalendarScreenRow).GetProperties().Select(property => property.Name).ToList();
        Assert.NotEmpty(members);
        Assert.DoesNotContain(members, name => name.Contains("Plan", StringComparison.OrdinalIgnoreCase) && name != "Title");
    }
}
