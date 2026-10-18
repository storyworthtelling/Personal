using Constitution.Domain.Body;
using Constitution.Domain.Body.Screens;
using Constitution.Domain.Health;
using Constitution.Domain.Voice;
using Constitution.Tests.Body;

namespace Constitution.Tests.BodyScreens;

/// <summary>
/// C-7.3.4, book page 16 (second phone): a tap on a chart opens it larger, with the same window switch,
/// the latest value and its date, the signal's own sentence against the owner's own past, and plain
/// source notes.
/// </summary>
public class SignalPageModelTests
{
    private static SignalPageModel Sleep(BodyWindow window = BodyWindow.Month, bool bedChosen = false) =>
        SignalPageModel.Build(ScreenData.Inputs(ScreenData.PageSixteen(), window, bedChosen), BodySignal.Sleep);

    [Fact]
    public void CarriesValueDateAndSource()
    {
        var page = Sleep();

        Assert.Equal("Sleep", page.Title);
        Assert.Equal("6.2 h", page.ValueText);
        Assert.Equal("last night", page.WhenText);
        Assert.Equal(ScreenData.Today, page.LatestDay);
        Assert.Equal("Tuesday, Nov 17", page.DateText);
        Assert.Equal(["From Health Connect."], page.SourceNotes);
    }

    [Fact]
    public void TheBedIsNamedWhenItStandsIn()
    {
        var page = Sleep(bedChosen: true);

        Assert.Equal(
            ["From Health Connect.", "The Sleep Number bed fills in nights the watch read as zero."],
            page.SourceNotes);
    }

    [Fact]
    public void AWorkedOutSignalSaysWhereItCameFrom()
    {
        var readings = ScreenData.TwoMonths(BodySignal.Strength, 100, 110);

        var page = SignalPageModel.Build(ScreenData.Inputs(readings), BodySignal.Strength);

        Assert.Equal(["Worked out from the workout pages."], page.SourceNotes);
    }

    [Fact]
    public void ThePageKeepsTheSameWindowSwitch()
    {
        var page = Sleep(BodyWindow.Quarter);

        Assert.Equal(BodyWindow.Quarter, page.Window);
        Assert.Equal(("PERIOD", "QUARTER"), page.Head.Fields[2]);
    }

    [Fact]
    public void TheSentenceIsSetAgainstTheOwnersOwnPast()
    {
        var page = Sleep();

        Assert.NotNull(page.Seat);
        Assert.Equal(VoiceSeat.SignalSentence, page.Seat!.Seat);
        Assert.Equal("Sleep: 6.2 hours last night, 40 minutes less than October's average.", page.Seat.Fallback);
        Assert.Contains(page.Seat.Facts, fact => fact.Contains("their own October average", StringComparison.Ordinal));
        Assert.Contains("The comparison is with their own past only.", page.Seat.Facts);
    }

    [Fact]
    public void TheQuarterIsSetAgainstTheQuarterBefore()
    {
        var readings = BodyData.Daily(BodySignal.Sleep, ScreenData.Today, 90, 6.2).ToList();
        readings.AddRange(BodyData.Daily(BodySignal.Sleep, ScreenData.Today.AddDays(-90), 90, 6.87));

        var page = SignalPageModel.Build(ScreenData.Inputs(readings, BodyWindow.Quarter), BodySignal.Sleep);

        Assert.Equal("Sleep: 6.2 hours last night, 40 minutes less than last quarter's average.", page.Seat!.Fallback);
    }

    [Fact]
    public void CaloriesHaveNoSeatBecauseTheyHaveNoSeatUnit()
    {
        var readings = ScreenData.TwoMonths(BodySignal.Calories, 2000, 2100);

        var page = SignalPageModel.Build(ScreenData.Inputs(readings), BodySignal.Calories);

        Assert.Null(page.Seat);
        Assert.Equal("2,100 kcal", page.ValueText);
    }

    [Fact]
    public void ASignalWithNothingEarlierHasNoSentenceToSay()
    {
        var readings = BodyData.Daily(BodySignal.Sleep, ScreenData.Today, 10, 6.2);

        var page = SignalPageModel.Build(ScreenData.Inputs(readings), BodySignal.Sleep);

        Assert.Null(page.Seat);
        Assert.Equal("6.2 h", page.ValueText);
    }

    [Fact]
    public void AnUnallowedSignalsPageCarriesTheAsk()
    {
        var inputs = ScreenData.Inputs(
            ScreenData.PageSixteen(),
            choices: ScreenData.AllGranted().With(BodySignal.HeartRateVariability, SignalChoice.NotAsked),
            isGranted: signal => signal != BodySignal.HeartRateVariability);

        var page = SignalPageModel.Build(inputs, BodySignal.HeartRateVariability);

        Assert.Equal(SignalCardState.AskPermission, page.State);
        Assert.Equal(AskKind.FirstOpen, page.Ask.Kind);
        Assert.Equal("Allow in Health Connect", page.Ask.BarLabel);
        Assert.Null(page.Seat);
        Assert.Empty(page.SourceNotes);
    }

    [Fact]
    public void AnOfflineBedStillShowsTheLastReadingAndSaysWhy()
    {
        var last = new DateOnly(2026, 11, 13);
        var readings = BodyData.Daily(BodySignal.Sleep, last, 30, 6.2).ToList();
        readings.AddRange(BodyData.Daily(BodySignal.Sleep, last.AddDays(-30), 30, 6.87));

        var page = SignalPageModel.Build(ScreenData.Inputs(readings, bedChosen: true), BodySignal.Sleep);

        Assert.Equal(SignalCardState.WaitingForBed, page.State);
        Assert.Equal("Thursday night", page.WhenText);
        Assert.Contains("The bed hasn’t reported since Thursday night.", page.SourceNotes);
    }
}
