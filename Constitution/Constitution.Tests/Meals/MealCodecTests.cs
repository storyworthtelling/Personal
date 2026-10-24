using Constitution.Domain.Meals;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlanning.MealData;

namespace Constitution.Tests.MealPlanning;

public class MealCodecTests
{
    [Fact]
    public void APlannedWeekRoundTrips()
    {
        var week = MealPlanner.Plan(Request(childrenHome: AllEvenings(), soon: [Id("onion-tart")]));

        var read = Assert.IsType<MealWeekRead.Read>(MealWeekCodec.Read(MealWeekCodec.Write(week)));

        Assert.Equal(MealWeekCodec.Write(week), MealWeekCodec.Write(read.Week));
        Assert.Equal(week.Dinners.Select(d => (d.Day, d.Kind, d.Recipe, d.Cook, d.CookStart, d.CookEnd, d.IsStretch, d.ChildrenEatDifferently, d.Reason)), read.Week.Dinners.Select(d => (d.Day, d.Kind, d.Recipe, d.Cook, d.CookStart, d.CookEnd, d.IsStretch, d.ChildrenEatDifferently, d.Reason)));
    }

    [Fact]
    public void TheCookBlockKeepsItsOffset()
    {
        var week = MealPlanner.Plan(Request());

        var read = Assert.IsType<MealWeekRead.Read>(MealWeekCodec.Read(MealWeekCodec.Write(week)));

        Assert.Equal(TimeSpan.FromHours(-4), read.Week.Dinners[0].CookEnd!.Value.Offset);
        Assert.Equal(TimeSpan.FromHours(-5), read.Week.Dinners[6].CookEnd!.Value.Offset);
    }

    [Fact]
    public void EveryKindRoundTrips()
    {
        var dinners = new[]
        {
            DinnerChoice.Leftovers(Monday, "Last night's."),
            DinnerChoice.EatOut(Monday.AddDays(1), "Out."),
            DinnerChoice.NothingPlanned(Monday.AddDays(2), "Free."),
            DinnerChoice.Cooked(Monday.AddDays(3), Id("onion-tart"), Lee, Edt(10, 29, 17), Edt(10, 29, 18), true, true, "New."),
            DinnerChoice.NothingPlanned(Monday.AddDays(4), "Free."),
            DinnerChoice.NothingPlanned(Monday.AddDays(5), "Free."),
            DinnerChoice.NothingPlanned(Monday.AddDays(6), "Free."),
        };
        var week = MealWeek.Planned(Monday, dinners, ["A note."]);

        var read = Assert.IsType<MealWeekRead.Read>(MealWeekCodec.Read(MealWeekCodec.Write(week)));

        Assert.Equal(dinners.Select(d => d.Kind), read.Week.Dinners.Select(d => d.Kind));
        Assert.True(read.Week.Dinners[3].IsStretch);
        Assert.True(read.Week.Dinners[3].ChildrenEatDifferently);
        Assert.Equal(["A note."], read.Week.Notes);
    }

    [Fact]
    public void AStoppedWeekRoundTrips()
    {
        var week = MealWeek.NotPlanned(Monday, MealStop.CalendarUnreadable, "A calendar could not be read.");

        var read = Assert.IsType<MealWeekRead.Read>(MealWeekCodec.Read(MealWeekCodec.Write(week)));

        Assert.False(read.Week.IsPlanned);
        Assert.Equal(MealStop.CalendarUnreadable, read.Week.Stop);
        Assert.Equal("A calendar could not be read.", read.Week.StopReason);
    }

    [Fact]
    public void TheStoredWordsAreFrozenLiterals()
    {
        var json = MealWeekCodec.Write(MealPlanner.Plan(Request()));
        var stopped = MealWeekCodec.Write(MealWeek.NotPlanned(Monday, MealStop.BoxTooSmall, "Few."));

        Assert.Contains("\"kind\":\"cooked\"", json, StringComparison.Ordinal);
        Assert.Contains("\"weekStart\":\"2026-10-26\"", json, StringComparison.Ordinal);
        Assert.Contains("\"stop\":\"boxTooSmall\"", stopped, StringComparison.Ordinal);
        Assert.DoesNotContain("rigin", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"weekStart\":\"2026-10-27\",\"dinners\":[]}")]
    [InlineData("{\"version\":1,\"weekStart\":\"2026-10-26\",\"dinners\":[{\"day\":\"2026-10-26\",\"kind\":\"banquet\",\"reason\":\"x\"}]}")]
    public void AnythingElseIsUnreadableNeverEmpty(string? json)
    {
        var read = MealWeekCodec.Read(json);

        var unreadable = Assert.IsType<MealWeekRead.Unreadable>(read);
        Assert.False(string.IsNullOrWhiteSpace(unreadable.Reason));
    }

    [Fact]
    public void AnUnreadableReasonQuotesNothing()
    {
        var read = Assert.IsType<MealWeekRead.Unreadable>(MealWeekCodec.Read("{secret-looking-text"));

        Assert.DoesNotContain("secret", read.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownFieldsAreTolerated()
    {
        var json = MealWeekCodec.Write(MealWeek.NotPlanned(Monday, MealStop.BoxTooSmall, "Few.")).Replace("{\"version\"", "{\"later\":{\"x\":1},\"version\"", StringComparison.Ordinal);

        Assert.IsType<MealWeekRead.Read>(MealWeekCodec.Read(json));
    }

    [Fact]
    public void HistoryRoundTrips()
    {
        var meals = Cooked(("tomato-pasta", 3), ("lentil-curry", 10));

        var read = MealHistoryCodec.Read(MealHistoryCodec.Write(meals));

        Assert.True(read.IsReadable);
        Assert.Equal(meals, read.Meals);
    }

    [Fact]
    public void HistoryWordsAreFrozen()
    {
        var json = MealHistoryCodec.Write(Cooked(("tomato-pasta", 3)));

        Assert.Equal("{\"version\":1,\"meals\":[{\"recipe\":\"tomato-pasta\",\"day\":\"2026-10-23\"}]}", json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nope")]
    [InlineData("{\"version\":1,\"meals\":[{\"recipe\":\"Bad Id\",\"day\":\"2026-10-23\"}]}")]
    [InlineData("{\"version\":1,\"meals\":[{\"recipe\":\"ok\",\"day\":\"yesterday\"}]}")]
    public void HistoryThatCannotBeReadIsUnreadableNotEmpty(string? json)
    {
        var read = MealHistoryCodec.Read(json);

        Assert.False(read.IsReadable);
        Assert.Empty(read.Meals);
    }

    [Fact]
    public void AnEmptyHistoryIsReadableAndEmpty()
    {
        var read = MealHistoryCodec.Read(MealHistoryCodec.Write([]));

        Assert.True(read.IsReadable);
        Assert.Empty(read.Meals);
    }
}
