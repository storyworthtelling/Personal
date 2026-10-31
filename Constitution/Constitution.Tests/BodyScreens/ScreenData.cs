using Constitution.Domain.Body;
using Constitution.Domain.Body.Screens;
using Constitution.Domain.Health;
using Constitution.Tests.Body;

namespace Constitution.Tests.BodyScreens;

/// <summary>
/// Invented readings and inputs for the Body tab's tests. Today is a Tuesday, so "Thursday night" and
/// "yesterday" are both unambiguous; every figure is made up, nobody's real one.
/// </summary>
internal static class ScreenData
{
    public static readonly DateOnly Today = BodyData.Today;

    /// <summary>The month before at one value and the month ending <paramref name="endingOn"/> at another.</summary>
    public static List<BodyReading> TwoMonths(BodySignal signal, double previous, double current, DateOnly? endingOn = null)
    {
        var end = endingOn ?? Today;
        var readings = BodyData.Daily(signal, end, 30, current).ToList();
        readings.AddRange(BodyData.Daily(signal, end.AddDays(-30), 30, previous));
        return readings;
    }

    /// <summary>The page 16 household: sleep slipped, weight and resting heart rate eased, steps rose.</summary>
    public static List<BodyReading> PageSixteen()
    {
        var readings = TwoMonths(BodySignal.Sleep, 6.87, 6.2);
        readings.AddRange(TwoMonths(BodySignal.Weight, 181.4, 179.2));
        readings.AddRange(TwoMonths(BodySignal.RestingHeartRate, 63, 59));
        readings.AddRange(TwoMonths(BodySignal.Steps, 5900, 7400, Today.AddDays(-1)));
        return readings;
    }

    public static HealthChoices AllGranted() =>
        BodySignals.HealthConnect.Aggregate(HealthChoices.Empty, (choices, signal) => choices.With(signal, SignalChoice.Granted));

    public static BodyTabInputs Inputs(
        IEnumerable<BodyReading> readings,
        BodyWindow window = BodyWindow.Month,
        bool bedChosen = false,
        HealthChoices? choices = null,
        Func<BodySignal, bool>? isGranted = null,
        IReadOnlySet<BodySignal>? revoked = null,
        DateOnly? today = null) =>
        new(
            readings.ToList(),
            window,
            today ?? Today,
            choices ?? AllGranted(),
            isGranted ?? (_ => true),
            bedChosen,
            revoked ?? new HashSet<BodySignal>());

    public static SignalCard CardOf(BodyTabModel model, BodySignal signal) =>
        model.Cards.Single(card => card.Signal == signal);
}
