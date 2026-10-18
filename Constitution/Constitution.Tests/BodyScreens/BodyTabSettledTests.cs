using Constitution.Domain.Body;
using Constitution.Tests.Body;
using Constitution.Domain.Body.Screens;
using Constitution.Domain.Health;

namespace Constitution.Tests.BodyScreens;

/// <summary>Review S5 and S7 on the screens: a part-counted day is not shown, and a bed-only sleeper is not asked for Health Connect.</summary>
public class BodyTabSettledTests
{
    private static List<BodyReading> StepsWith(double today)
    {
        var readings = ScreenData.TwoMonths(BodySignal.Steps, 5900, 7400, ScreenData.Today.AddDays(-1));
        readings.Add(BodyData.Reading(BodySignal.Steps, ScreenData.Today, today));
        return readings;
    }

    [Fact]
    public void StepsShowYesterdayNotTheDayInProgress()
    {
        var card = ScreenData.CardOf(BodyTabModel.Build(ScreenData.Inputs(StepsWith(612))), BodySignal.Steps);

        var yesterdayOnly = ScreenData.CardOf(
            BodyTabModel.Build(ScreenData.Inputs(ScreenData.TwoMonths(BodySignal.Steps, 5900, 7400, ScreenData.Today.AddDays(-1)))),
            BodySignal.Steps);
        Assert.Equal(yesterdayOnly.ValueText, card.ValueText);
        Assert.Equal("yesterday", card.WhenText);
    }

    [Fact]
    public void ActiveMinutesShowYesterdayToo()
    {
        var readings = ScreenData.TwoMonths(BodySignal.ActiveMinutes, 30, 41, ScreenData.Today.AddDays(-1));
        readings.Add(BodyData.Reading(BodySignal.ActiveMinutes, ScreenData.Today, 2));

        var card = ScreenData.CardOf(BodyTabModel.Build(ScreenData.Inputs(readings)), BodySignal.ActiveMinutes);

        var yesterdayOnly = ScreenData.CardOf(
            BodyTabModel.Build(ScreenData.Inputs(ScreenData.TwoMonths(BodySignal.ActiveMinutes, 30, 41, ScreenData.Today.AddDays(-1)))),
            BodySignal.ActiveMinutes);
        Assert.Equal(yesterdayOnly.ValueText, card.ValueText);
    }

    [Fact]
    public void ASpotMeasureStillShowsToday()
    {
        var readings = ScreenData.TwoMonths(BodySignal.Weight, 181.4, 179.2);

        var card = ScreenData.CardOf(BodyTabModel.Build(ScreenData.Inputs(readings)), BodySignal.Weight);

        Assert.Equal("this morning", card.WhenText);
    }

    [Fact]
    public void TheChartDoesNotDrawTheDayInProgress()
    {
        var card = ScreenData.CardOf(BodyTabModel.Build(ScreenData.Inputs(StepsWith(612))), BodySignal.Steps);

        Assert.DoesNotContain(card.Readings, reading => reading.Day == ScreenData.Today);
    }

    [Fact]
    public void TheSeatFactsDoNotMoveWhileTodayFillsIn()
    {
        var morning = BodyTabModel.Build(ScreenData.Inputs(StepsWith(612)));
        var evening = BodyTabModel.Build(ScreenData.Inputs(StepsWith(9400)));

        Assert.NotNull(morning.Monthly);
        Assert.Equal(morning.Monthly!.Facts, evening.Monthly!.Facts);
        Assert.Equal(morning.Monthly.Fallback, evening.Monthly.Fallback);
    }

    [Fact]
    public void TheSignalPageFollowsTheSameRule()
    {
        var page = SignalPageModel.Build(ScreenData.Inputs(StepsWith(612)), BodySignal.Steps);

        Assert.Equal(ScreenData.Today.AddDays(-1), page.LatestDay);
        Assert.DoesNotContain("612", page.ValueText, StringComparison.Ordinal);
    }

    [Fact]
    public void ABedOnlySleeperIsNotAskedForHealthConnect()
    {
        var readings = ScreenData.TwoMonths(BodySignal.Sleep, 7.0, 6.5);
        var inputs = ScreenData.Inputs(
            readings, bedChosen: true, choices: HealthChoices.Empty, isGranted: signal => signal != BodySignal.Sleep);

        var card = ScreenData.CardOf(BodyTabModel.Build(inputs), BodySignal.Sleep);

        Assert.Equal(SignalCardState.Charted, card.State);
        Assert.NotEqual(SignalCardState.AskPermission, card.State);
    }

    [Fact]
    public void WithoutASideSleepStillAsksForHealthConnect()
    {
        var inputs = ScreenData.Inputs(
            ScreenData.TwoMonths(BodySignal.Sleep, 7.0, 6.5), bedChosen: false, choices: HealthChoices.Empty,
            isGranted: signal => signal != BodySignal.Sleep);

        Assert.Equal(SignalCardState.AskPermission, ScreenData.CardOf(BodyTabModel.Build(inputs), BodySignal.Sleep).State);
    }

    [Fact]
    public void TheBedSpeaksForSleepOnlyNotForTheRest()
    {
        var inputs = ScreenData.Inputs([], bedChosen: true, choices: HealthChoices.Empty, isGranted: _ => false);

        Assert.Equal(SignalCardState.AskPermission, ScreenData.CardOf(BodyTabModel.Build(inputs), BodySignal.Weight).State);
    }
}
