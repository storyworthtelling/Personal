using Constitution.Domain.Body;
using Constitution.Domain.Body.Screens;

namespace Constitution.Tests.BodyScreens;

/// <summary>
/// C-7.3.3, book page 16: every chart wears a printed form head in mono, signal and units and period,
/// the way a doctor's chart is headed. The wording is the book's, character for character.
/// </summary>
public class FormHeadTests
{
    [Fact]
    public void FormHeadNamesSignalUnitsAndPeriod()
    {
        var head = FormHead.For(BodySignal.Sleep, BodyWindow.Month);

        Assert.Equal([("SIGNAL", "SLEEP"), ("UNITS", "HOURS"), ("PERIOD", "MONTH")], head.Fields);
    }

    [Theory]
    [InlineData(BodySignal.Weight, "WEIGHT", "LB")]
    [InlineData(BodySignal.RestingHeartRate, "RESTING HEART RATE", "BPM")]
    [InlineData(BodySignal.Steps, "STEPS", "A DAY")]
    [InlineData(BodySignal.HeartRateVariability, "HEART RATE VARIABILITY", "MS")]
    public void EverySignalIsHeadedInItsOwnWords(BodySignal signal, string name, string units)
    {
        var head = FormHead.For(signal, BodyWindow.Month);

        Assert.Equal(name, head.Fields[0].Value);
        Assert.Equal(units, head.Fields[1].Value);
    }

    [Theory]
    [InlineData(BodyWindow.Month, "MONTH")]
    [InlineData(BodyWindow.Quarter, "QUARTER")]
    [InlineData(BodyWindow.Year, "YEAR")]
    public void ThePeriodIsTheWindowWord(BodyWindow window, string period)
    {
        Assert.Equal(("PERIOD", period), FormHead.For(BodySignal.Sleep, window).Fields[2]);
    }

    [Fact]
    public void AChartThatAsksForPermissionHasNoPeriodYet()
    {
        var head = FormHead.For(BodySignal.HeartRateVariability, window: null);

        Assert.Equal([("SIGNAL", "HEART RATE VARIABILITY"), ("UNITS", "MS")], head.Fields);
    }
}
