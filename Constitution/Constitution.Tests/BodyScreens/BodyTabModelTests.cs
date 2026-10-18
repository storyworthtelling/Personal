using Constitution.Domain.Body;
using Constitution.Domain.Body.Screens;
using Constitution.Domain.Health;
using Constitution.Domain.Voice;
using Constitution.Tests.Body;

namespace Constitution.Tests.BodyScreens;

/// <summary>
/// C-7.3.3, book page 16: Body is a window switch, the month's sentence, then one chart per signal with
/// its latest value in mono and the change in words, the worsening first. The model decides all of it;
/// the head only paints.
/// </summary>
public class BodyTabModelTests
{
    private static BodyTabModel PageSixteen(BodyWindow window = BodyWindow.Month) =>
        BodyTabModel.Build(ScreenData.Inputs(ScreenData.PageSixteen(), window));

    // ---- the change, in words ----

    [Fact]
    public void ChangeIsInWords()
    {
        var model = PageSixteen();

        Assert.Equal("down about 40 minutes a night", ScreenData.CardOf(model, BodySignal.Sleep).ChangeText);
        Assert.Equal("down 2.2 lb", ScreenData.CardOf(model, BodySignal.Weight).ChangeText);
        Assert.Equal("down 4 bpm", ScreenData.CardOf(model, BodySignal.RestingHeartRate).ChangeText);
        Assert.Equal("up about 1,500 steps a day", ScreenData.CardOf(model, BodySignal.Steps).ChangeText);
    }

    [Fact]
    public void TheChangeIsTheSignalChangesOwnWords()
    {
        var model = PageSixteen();
        var change = SignalChange.From(ScreenData.PageSixteen(), BodySignal.Weight, BodyWindow.Month, ScreenData.Today)!;

        Assert.Equal(change.Words, ScreenData.CardOf(model, BodySignal.Weight).ChangeText);
    }

    [Fact]
    public void ASteadySignalSaysSoInWords()
    {
        var readings = ScreenData.TwoMonths(BodySignal.Weight, 179.0, 179.2);

        var card = ScreenData.CardOf(BodyTabModel.Build(ScreenData.Inputs(readings)), BodySignal.Weight);

        Assert.Equal("about the same", card.ChangeText);
    }

    [Fact]
    public void ASignalWithNothingEarlierToCompareSaysThat()
    {
        var readings = BodyData.Daily(BodySignal.Weight, ScreenData.Today, 10, 179.2);

        var card = ScreenData.CardOf(BodyTabModel.Build(ScreenData.Inputs(readings)), BodySignal.Weight);

        Assert.Equal(SignalCardState.Charted, card.State);
        Assert.Equal("179.2 lb", card.ValueText);
        Assert.Equal("nothing to compare with yet", card.ChangeText);
    }

    // ---- the latest value, in mono ----

    [Fact]
    public void LatestValueIsInMonoWithWhenItWasRead()
    {
        var model = PageSixteen();

        AssertLatest(model, BodySignal.Sleep, "6.2 h", "last night");
        AssertLatest(model, BodySignal.Weight, "179.2 lb", "this morning");
        AssertLatest(model, BodySignal.RestingHeartRate, "59 bpm", "today");
        AssertLatest(model, BodySignal.Steps, "7,400", "yesterday");
    }

    private static void AssertLatest(BodyTabModel model, BodySignal signal, string value, string when)
    {
        var card = ScreenData.CardOf(model, signal);
        Assert.Equal(value, card.ValueText);
        Assert.Equal(when, card.WhenText);
    }

    [Fact]
    public void AReadingFromEarlierThisWeekIsNamedByItsDay()
    {
        // Nothing since Friday: Tuesday's page says "Friday", not a date it would have to be worked out from.
        var readings = ScreenData.TwoMonths(BodySignal.Weight, 181.4, 179.2, new DateOnly(2026, 11, 13));

        var card = ScreenData.CardOf(BodyTabModel.Build(ScreenData.Inputs(readings)), BodySignal.Weight);

        Assert.Equal("Friday", card.WhenText);
    }

    // ---- the form head ----

    [Fact]
    public void EachCardIsHeadedWithItsSignalUnitsAndPeriod()
    {
        var month = ScreenData.CardOf(PageSixteen(), BodySignal.Sleep);
        var quarter = ScreenData.CardOf(PageSixteen(BodyWindow.Quarter), BodySignal.Sleep);

        Assert.Equal(FormHead.For(BodySignal.Sleep, BodyWindow.Month), month.Head);
        Assert.Equal(("PERIOD", "QUARTER"), quarter.Head.Fields[2]);
    }

    // ---- the order ----

    [Fact]
    public void TheWorseningSignalComesFirst()
    {
        var model = PageSixteen();

        // Sleep fell (worse); weight, resting heart rate and steps moved the better way.
        Assert.Equal(BodySignal.Sleep, model.Cards[0].Signal);
    }

    [Fact]
    public void CardsFollowSignalOrderThenTheRest()
    {
        var model = PageSixteen();
        var charted = model.Cards.Where(card => card.State == SignalCardState.Charted).Select(card => card.Signal).ToList();
        var expected = SignalOrder.Order(
            new[] { BodySignal.Sleep, BodySignal.Weight, BodySignal.RestingHeartRate, BodySignal.Steps }
                .Select(signal => SignalChange.From(ScreenData.PageSixteen(), signal, BodyWindow.Month, ScreenData.Today)!))
            .Select(change => change.Signal).ToList();

        Assert.Equal(expected, charted);

        // The three the owner has no readings for wait at the foot, in Body's own order.
        Assert.Equal(
            [BodySignal.BodyComposition, BodySignal.HeartRateVariability, BodySignal.ActiveMinutes],
            model.Cards.Skip(4).Select(card => card.Signal).ToList());
    }

    [Fact]
    public void EverySignalHealthConnectSuppliesHasAChart()
    {
        var model = PageSixteen();

        Assert.Equal(BodySignals.HealthConnect.Count, model.Cards.Count);
        Assert.All(model.Cards.Skip(4), card => Assert.Equal(SignalCardState.NoReadings, card.State));
    }

    [Fact]
    public void AWorkedOutSignalShowsOnlyOnceItHasReadings()
    {
        var withStrength = ScreenData.PageSixteen();
        withStrength.AddRange(ScreenData.TwoMonths(BodySignal.Strength, 100, 110));

        var model = BodyTabModel.Build(ScreenData.Inputs(withStrength));

        Assert.Contains(model.Cards, card => card.Signal == BodySignal.Strength);
        Assert.DoesNotContain(PageSixteen().Cards, card => card.Signal is BodySignal.Strength or BodySignal.Calories);
    }

    // ---- the window ----

    [Fact]
    public void TheWindowIsCarriedForTheSwitch()
    {
        Assert.Equal(BodyWindow.Month, PageSixteen().Window);
        Assert.Equal(BodyWindow.Year, PageSixteen(BodyWindow.Year).Window);
        Assert.Equal(BodyWindows.All, BodyTabModel.Windows);
        Assert.Equal(["Month", "Quarter", "Year"], BodyTabModel.WindowLabels);
    }

    [Fact]
    public void TheQuarterComparesQuartersNotMonths()
    {
        var readings = ScreenData.TwoMonths(BodySignal.Weight, 181.4, 179.2);
        readings.AddRange(BodyData.Daily(BodySignal.Weight, ScreenData.Today.AddDays(-60), 30, 181.4));
        readings.AddRange(BodyData.Daily(BodySignal.Weight, ScreenData.Today.AddDays(-90), 90, 190));

        var card = ScreenData.CardOf(
            BodyTabModel.Build(ScreenData.Inputs(readings, BodyWindow.Quarter)), BodySignal.Weight);
        var change = SignalChange.From(readings, BodySignal.Weight, BodyWindow.Quarter, ScreenData.Today)!;

        Assert.Equal(change.Words, card.ChangeText);
    }

    // ---- permission states on the cards ----

    [Fact]
    public void AnUnallowedSignalWaitsForTheOwnerToAllowIt()
    {
        var inputs = ScreenData.Inputs(
            ScreenData.PageSixteen(),
            choices: ScreenData.AllGranted().With(BodySignal.HeartRateVariability, SignalChoice.NotAsked),
            isGranted: signal => signal != BodySignal.HeartRateVariability);

        var card = ScreenData.CardOf(BodyTabModel.Build(inputs), BodySignal.HeartRateVariability);

        Assert.Equal(SignalCardState.AskPermission, card.State);
        Assert.Equal(("SIGNAL", "HEART RATE VARIABILITY"), card.Head.Fields[0]);
        Assert.DoesNotContain(card.Head.Fields, field => field.Field == "PERIOD");
    }

    [Fact]
    public void ARevokedSignalsChartWaits()
    {
        var inputs = ScreenData.Inputs(
            ScreenData.PageSixteen(),
            isGranted: signal => signal != BodySignal.Steps);

        var card = ScreenData.CardOf(BodyTabModel.Build(inputs), BodySignal.Steps);

        Assert.Equal(SignalCardState.Revoked, card.State);
        Assert.Empty(card.ValueText);
    }

    // ---- the monthly sentence ----

    [Fact]
    public void TheMonthlySeatIsBodysOwn()
    {
        var model = PageSixteen();

        Assert.NotNull(model.Monthly);
        Assert.Equal(VoiceSeat.BodyMonthly, model.Monthly!.Seat);
    }

    [Fact]
    public void TheMonthlySentenceIsAlwaysTheMonthsWhateverTheSwitchSays()
    {
        Assert.Equal(PageSixteen().Monthly!.Facts, PageSixteen(BodyWindow.Year).Monthly!.Facts);
    }

    [Fact]
    public void TheNeutralMonthlyIsBuiltFromTheSameFactsAsTheVoicedOne()
    {
        var readings = ScreenData.TwoMonths(BodySignal.Sleep, 6.87, 6.2);
        readings.AddRange(ScreenData.TwoMonths(BodySignal.Steps, 8100, 8000));
        readings.AddRange(ScreenData.TwoMonths(BodySignal.RestingHeartRate, 59, 58));

        var model = BodyTabModel.Build(ScreenData.Inputs(readings));

        Assert.Equal("Sleep is down this month. The other signals are close to last month.", model.Monthly!.Fallback);
    }

    [Fact]
    public void ThePlainPageHasNoMonthlySeatWhenThereIsNothingToCompare()
    {
        var model = BodyTabModel.Build(ScreenData.Inputs(BodyData.Daily(BodySignal.Weight, ScreenData.Today, 5, 179.2)));

        Assert.Null(model.Monthly);
        Assert.Equal("Nothing to compare yet. Body reads from the day it was installed.", model.NothingToCompare);
    }

    [Fact]
    public void ARisingWeightIsCalledWorseNotSoftened()
    {
        var readings = ScreenData.TwoMonths(BodySignal.Weight, 175, 179);

        var model = BodyTabModel.Build(ScreenData.Inputs(readings));

        Assert.Equal("Weight is up this month.", model.Monthly!.Fallback);
        Assert.Contains(model.Monthly.Facts, fact => fact.Contains("which is worse", StringComparison.Ordinal));
    }

    // ---- the page keeps the chart honest ----

    [Fact]
    public void NoScreenNameReadsAsAJudgement()
    {
        var names = typeof(BodyTabModel).Assembly.GetTypes()
            .Where(type => type.Namespace == "Constitution.Domain.Body.Screens" && !type.Name.StartsWith('<'))
            .SelectMany(type => new[] { type.Name }.Concat(
                type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.DeclaredOnly)
                    .Where(member => !member.Name.StartsWith('<') && !member.Name.StartsWith('.'))
                    .Select(member => member.Name)))
            .Distinct()
            .ToList();

        Assert.True(names.Count >= 40, $"The guard found only {names.Count} names; it is not reading the real model.");
        var offenders = names.Where(HonestChartTests.IsJudgement).ToList();
        Assert.True(offenders.Count == 0, "Screen names that read as a judgement: " + string.Join(", ", offenders));
    }
}
