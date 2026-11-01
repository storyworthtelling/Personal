using System.Globalization;
using Constitution.Domain.Body;

namespace Constitution.Tests.Body;

/// <summary>
/// O7, the runtime half (LOG-LOOP-PLAN.md §4.6): the Body trail now leaves the phone in the weekly log upload, so
/// a body sync over invented sentinel readings — an odd weight, a resting heart rate, a variability, a night's
/// hours — must leave log lines that carry none of those numbers. Events and counts only; the reading itself stays
/// home. <see cref="BodyStaysHomeTests"/> is the static half: the log seams take a phrase, never a Body type.
/// </summary>
public class BodyLogsCarryNoValuesTests
{
    private const double Weight = 173.42;
    private const double RestingHeart = 58.37;
    private const double Variability = 41.93;
    private const double SleepHours = 7.83;

    [Fact]
    public async Task ASyncLogsWhatHappenedButNoReading()
    {
        var log = new CapturingBodyLog();
        var source = new FakeHealthSource()
            .Grant(BodySignal.Weight, Samples.Of(Samples.Today, Weight), Samples.Of(Samples.Today.AddDays(-1), Weight + 1))
            .Grant(BodySignal.RestingHeartRate, Samples.Of(Samples.Today, RestingHeart))
            .Grant(BodySignal.HeartRateVariability, Samples.Of(Samples.Today, Variability))
            .Grant(BodySignal.Sleep, Samples.Of(Samples.Today, SleepHours));
        source.BackgroundReadsGranted = true;
        var store = new MemoryBodyStore();
        store.SetInstallDay(Samples.Today.AddDays(-1));
        var reader = new SignalReader(store, source, () => Samples.Today, log, null);

        await new BodySync(reader, source, new CapturingSyncStatus(), log).RunInBackgroundAsync(default);
        await new BodySync(reader, source, new CapturingSyncStatus(), log).OpenTimeReadsAsync(default);

        Assert.NotEmpty(log.Lines);
        AssertNoSentinel(log.Lines);
    }

    [Fact]
    public async Task ABedNightFilledInLogsNoHours()
    {
        var log = new CapturingBodyLog();
        var filler = new FakeSleepFiller();
        filler.Nights[Samples.Today] = SleepHours;
        var source = new FakeHealthSource().Grant(BodySignal.Sleep);
        var store = new MemoryBodyStore();
        store.SetInstallDay(Samples.Today);

        await new SignalReader(store, source, () => Samples.Today, log, filler).ReadAsync(default);

        AssertNoSentinel(log.Lines);
    }

    private static void AssertNoSentinel(IEnumerable<string> lines)
    {
        var sentinels = new[] { Weight, Weight + 1, RestingHeart, Variability, SleepHours }
            .SelectMany(value => new[]
            {
                value.ToString(CultureInfo.InvariantCulture),
                value.ToString("0.0", CultureInfo.InvariantCulture),
                Math.Round(value).ToString(CultureInfo.InvariantCulture),
            })
            .ToList();
        foreach (var line in lines)
        {
            foreach (var sentinel in sentinels)
            {
                Assert.False(line.Contains(sentinel, StringComparison.Ordinal), $"A body value reached the log: '{line}' carries {sentinel}.");
            }
        }
    }
}
