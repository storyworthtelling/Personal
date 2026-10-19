using System.Reflection;
using Constitution.Domain.Body;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>
/// C-5.2.4 · Recovery, the reading half. The morning's resting heart rate, heart-rate variability and sleep are set
/// against the owner's <i>own</i> past (the Body principle: no norm, no population), and a short night, a low HRV or a
/// raised resting heart rate reads as under-recovered. When the morning's value was never read in the background (R5),
/// the last one read while the app was open is used, and the read says so.
///
/// <para><b>Device-flagged, and what that means here.</b> Every reading below is invented. These tests prove the arithmetic
/// and the fallbacks; they cannot say that Health Connect on this phone ever delivers a morning HRV, or that the
/// thresholds suit the owner's body. The numbers are named constants on <see cref="RecoveryReader"/> for that reason.</para>
/// </summary>
public class RecoveryReaderTests
{
    private static readonly DateOnly Morning = Day(2);

    private const double Rhr = 60;
    private const double Hrv = 50;
    private const double Sleep = 7.5;

    private static BodyReading R(BodySignal signal, DateOnly day, double value) => new(signal, day, value);

    /// <summary>A steady fortnight before the morning: the owner's own past.</summary>
    private static List<BodyReading> Past(int days = 14, DateOnly? before = null)
    {
        var end = (before ?? Morning).AddDays(-1);
        return Enumerable.Range(0, days)
            .SelectMany(back => new[]
            {
                R(BodySignal.RestingHeartRate, end.AddDays(-back), Rhr),
                R(BodySignal.HeartRateVariability, end.AddDays(-back), Hrv),
                R(BodySignal.Sleep, end.AddDays(-back), Sleep),
            })
            .ToList();
    }

    private static List<BodyReading> With(double? rhr = null, double? hrv = null, double? sleep = null, DateOnly? on = null, List<BodyReading>? past = null)
    {
        var readings = past ?? Past();
        var day = on ?? Morning;
        if (rhr is { } r)
        {
            readings.Add(R(BodySignal.RestingHeartRate, day, r));
        }

        if (hrv is { } h)
        {
            readings.Add(R(BodySignal.HeartRateVariability, day, h));
        }

        if (sleep is { } s)
        {
            readings.Add(R(BodySignal.Sleep, day, s));
        }

        return readings;
    }

    // ---- the verdict ----

    [Fact]
    public void AnOrdinaryMorningIsRecovered()
    {
        var read = RecoveryReader.Read(With(rhr: 61, hrv: 48, sleep: 7.0), Morning);

        Assert.Equal(RecoveryState.Recovered, read.State);
        Assert.Empty(read.Reasons);
        Assert.Empty(read.StaleSignals);
        Assert.Empty(read.MissingSignals);
    }

    [Fact]
    public void AnUnusuallyGoodMorningIsRecovered()
    {
        var read = RecoveryReader.Read(With(rhr: 54, hrv: 70, sleep: 9.0), Morning);

        Assert.Equal(RecoveryState.Recovered, read.State);
    }

    [Theory]
    [InlineData(6.4, RecoveryReason.ShortSleep)]
    [InlineData(5.0, RecoveryReason.ShortSleep)]
    public void ShortSleepIsUnderRecovery(double sleep, RecoveryReason reason)
    {
        var read = RecoveryReader.Read(With(rhr: Rhr, hrv: Hrv, sleep: sleep), Morning);

        Assert.Equal(RecoveryState.UnderRecovered, read.State);
        Assert.Equal([reason], read.Reasons.ToArray());
    }

    [Fact]
    public void AHeartRateVariabilityThatDroppedIsUnderRecovery()
    {
        var read = RecoveryReader.Read(With(rhr: Rhr, hrv: 38, sleep: Sleep), Morning);

        Assert.Equal(RecoveryState.UnderRecovered, read.State);
        Assert.Equal([RecoveryReason.LowHeartRateVariability], read.Reasons.ToArray());
    }

    [Fact]
    public void ARaisedRestingHeartRateIsUnderRecovery()
    {
        var read = RecoveryReader.Read(With(rhr: 66, hrv: Hrv, sleep: Sleep), Morning);

        Assert.Equal(RecoveryState.UnderRecovered, read.State);
        Assert.Equal([RecoveryReason.RaisedRestingHeartRate], read.Reasons.ToArray());
    }

    [Fact]
    public void AnyOneSignalIsEnough()
    {
        Assert.Equal(RecoveryState.UnderRecovered, RecoveryReader.Read(With(sleep: 6.0), Morning).State);
        Assert.Equal(RecoveryState.UnderRecovered, RecoveryReader.Read(With(hrv: 38), Morning).State);
        Assert.Equal(RecoveryState.UnderRecovered, RecoveryReader.Read(With(rhr: 66), Morning).State);
    }

    [Fact]
    public void TheEdgesAreInclusive()
    {
        var baseline = Sleep - RecoveryReader.ShortSleepHours;
        var rising = Rhr + RecoveryReader.RaisedRestingHeartRateBeats;

        Assert.Equal(RecoveryState.UnderRecovered, RecoveryReader.Read(With(sleep: baseline), Morning).State);
        Assert.Equal(RecoveryState.Recovered, RecoveryReader.Read(With(sleep: baseline + 0.1), Morning).State);
        Assert.Equal(RecoveryState.UnderRecovered, RecoveryReader.Read(With(rhr: rising), Morning).State);
        Assert.Equal(RecoveryState.Recovered, RecoveryReader.Read(With(rhr: rising - 0.5), Morning).State);
    }

    [Fact]
    public void SeveralSignalsNameSeveralReasonsInTheirFixedOrder()
    {
        var read = RecoveryReader.Read(With(rhr: 66, hrv: 38, sleep: 6.0), Morning);

        Assert.Equal(
            [RecoveryReason.LowHeartRateVariability, RecoveryReason.ShortSleep, RecoveryReason.RaisedRestingHeartRate],
            read.Reasons.ToArray());
    }

    [Fact]
    public void TwoSignalsFarOffAreAlarming()
    {
        var read = RecoveryReader.Read(With(rhr: 71, hrv: Hrv, sleep: 5.0), Morning);

        Assert.Equal(RecoveryState.Alarming, read.State);
        Assert.Equal([RecoveryReason.ShortSleep, RecoveryReason.RaisedRestingHeartRate], read.Reasons.ToArray());
    }

    [Fact]
    public void OneSignalFarOffIsNotAlarming()
    {
        Assert.Equal(RecoveryState.UnderRecovered, RecoveryReader.Read(With(sleep: 4.0), Morning).State);
        Assert.Equal(RecoveryState.UnderRecovered, RecoveryReader.Read(With(rhr: 80), Morning).State);
    }

    [Fact]
    public void ManySignalsOffALittleAreNotAlarming()
    {
        var read = RecoveryReader.Read(With(rhr: 66, hrv: 38, sleep: 6.0), Morning);

        Assert.Equal(RecoveryState.UnderRecovered, read.State);
    }

    [Fact]
    public void ASignalAloneCanBeJudgedWhenTheOthersAreMissing()
    {
        var past = Past().Where(item => item.Signal == BodySignal.Sleep).ToList();

        var read = RecoveryReader.Read(With(sleep: 5.5, past: past), Morning);

        Assert.Equal(RecoveryState.UnderRecovered, read.State);
        Assert.Equal([BodySignal.RestingHeartRate, BodySignal.HeartRateVariability], read.MissingSignals.ToArray());
    }

    // ---- NotEnoughToSay ----

    [Fact]
    public void NothingReadSaysNothing()
    {
        var read = RecoveryReader.Read([], Morning);

        Assert.Equal(RecoveryState.NotEnoughToSay, read.State);
        Assert.Equal(3, read.MissingSignals.Count);
    }

    [Fact]
    public void AFreshInstallHasNoPastToCompareWith()
    {
        // Six nights is not a baseline (the Body tab's own seven-day minimum).
        var read = RecoveryReader.Read(With(rhr: 80, hrv: 20, sleep: 3.0, past: Past(days: 6)), Morning);

        Assert.Equal(RecoveryState.NotEnoughToSay, read.State);
        Assert.Empty(read.Reasons);
        Assert.Equal(3, read.MissingSignals.Count);
    }

    [Fact]
    public void SevenDaysOfPastIsEnough()
    {
        var read = RecoveryReader.Read(With(sleep: 5.0, past: Past(days: 7)), Morning);

        Assert.Equal(RecoveryState.UnderRecovered, read.State);
    }

    [Fact]
    public void ThePastIsTheMonthBeforeAndNoFurther()
    {
        // A month and more ago the owner slept ten hours; it does not drag today's baseline.
        var past = Past(days: 14);
        past.AddRange(Enumerable.Range(0, 20).Select(back => R(BodySignal.Sleep, Morning.AddDays(-40 - back), 11.0)));

        var read = RecoveryReader.Read(With(sleep: 7.0, past: past), Morning);

        Assert.Equal(RecoveryState.Recovered, read.State);
    }

    [Fact]
    public void AReadingFromTheFutureIsNotUsed()
    {
        var readings = With(sleep: 7.5);
        readings.Add(R(BodySignal.Sleep, Morning.AddDays(1), 3.0));
        readings.Add(R(BodySignal.RestingHeartRate, Morning.AddDays(2), 99));

        var read = RecoveryReader.Read(readings, Morning);

        Assert.Equal(RecoveryState.Recovered, read.State);
    }

    [Fact]
    public void ReadingsThatAreNotRealNumbersAreIgnored()
    {
        var readings = With(sleep: 7.5);
        readings.Add(R(BodySignal.RestingHeartRate, Morning, double.NaN));
        readings.Add(R(BodySignal.HeartRateVariability, Morning, -4));
        readings.Add(R(BodySignal.Sleep, Morning.AddDays(-3), double.PositiveInfinity));

        var read = RecoveryReader.Read(readings, Morning);

        Assert.Equal(RecoveryState.Recovered, read.State);
    }

    [Fact]
    public void OtherSignalsAreNotRecoverySignals()
    {
        var readings = With(sleep: 7.5);
        readings.Add(R(BodySignal.Weight, Morning, 400));
        readings.Add(R(BodySignal.Steps, Morning, 1));

        Assert.Equal(RecoveryState.Recovered, RecoveryReader.Read(readings, Morning).State);
    }

    // ---- StaleValuesAreUsedAndNoted ----

    [Fact]
    public void StaleValuesAreUsedAndNoted()
    {
        // No background read this morning (R5): the last values read while the app was open are yesterday's and the day before's.
        var past = Past(before: Morning.AddDays(-1));
        var readings = With(rhr: 68, on: Morning.AddDays(-1), past: past);
        readings.Add(R(BodySignal.Sleep, Morning, 7.5));
        readings.Add(R(BodySignal.HeartRateVariability, Morning.AddDays(-2), 50));

        var read = RecoveryReader.Read(readings, Morning);

        Assert.Equal(RecoveryState.UnderRecovered, read.State);
        Assert.Equal([RecoveryReason.RaisedRestingHeartRate], read.Reasons.ToArray());
        Assert.True(read.UsedStaleValues);
        Assert.Contains(BodySignal.RestingHeartRate, read.StaleSignals);
        Assert.Contains(BodySignal.HeartRateVariability, read.StaleSignals);
        Assert.DoesNotContain(BodySignal.Sleep, read.StaleSignals);
        Assert.Empty(read.MissingSignals);
    }

    [Fact]
    public void AFreshReadIsNotStale()
    {
        var read = RecoveryReader.Read(With(rhr: Rhr, hrv: Hrv, sleep: Sleep), Morning);

        Assert.False(read.UsedStaleValues);
    }

    [Fact]
    public void TheNewestStaleValueWins()
    {
        var past = Past(before: Morning.AddDays(-3));
        var readings = With(sleep: 4.0, on: Morning.AddDays(-3), past: past);
        readings.Add(R(BodySignal.Sleep, Morning.AddDays(-1), 7.4));

        var read = RecoveryReader.Read(readings, Morning);

        Assert.Equal(RecoveryState.Recovered, read.State);
        Assert.Equal([BodySignal.Sleep], read.StaleSignals.ToArray());
    }

    [Fact]
    public void ThreeDaysOldIsStillUsedAndFourIsNot()
    {
        var threeOld = RecoveryReader.Read(With(sleep: 5.0, on: Morning.AddDays(-RecoveryReader.StaleDays), past: Past(before: Morning.AddDays(-RecoveryReader.StaleDays))), Morning);
        var tooOld = RecoveryReader.Read(With(sleep: 5.0, on: Morning.AddDays(-RecoveryReader.StaleDays - 1), past: Past(before: Morning.AddDays(-RecoveryReader.StaleDays - 1))), Morning);

        Assert.Equal(RecoveryState.UnderRecovered, threeOld.State);
        Assert.Equal([BodySignal.Sleep], threeOld.StaleSignals.ToArray());
        Assert.Equal(RecoveryState.NotEnoughToSay, tooOld.State);
        Assert.Contains(BodySignal.Sleep, tooOld.MissingSignals);
        Assert.Empty(tooOld.StaleSignals);
    }

    [Fact]
    public void AStaleValueIsJudgedAgainstThePastBeforeIt()
    {
        // The stale night was a normal one for a person whose weeks run short: judged against the days before *it*, not before this morning.
        var past = Enumerable.Range(0, 14).Select(back => R(BodySignal.Sleep, Morning.AddDays(-2 - back), 6.0)).ToList();
        past.Add(R(BodySignal.Sleep, Morning.AddDays(-1), 6.0));

        var read = RecoveryReader.Read(past, Morning);

        Assert.Equal(RecoveryState.Recovered, read.State);
    }

    // ---- stays home ----

    [Fact]
    public void TheReadCarriesWordsAndNeverAValue()
    {
        foreach (var type in new[] { typeof(RecoveryRead), typeof(RecoverySwapNote), typeof(RecoveryAdjusted) })
        {
            foreach (var member in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var memberType = member.PropertyType;
                Assert.False(
                    memberType == typeof(double) || memberType == typeof(float) || memberType == typeof(decimal) || memberType == typeof(BodyReading),
                    $"{type.Name}.{member.Name} could hold a Body value; a recovery read says words, not numbers (D98).");
            }
        }
    }

    [Fact]
    public void TheWorkoutsFilesHaveNoWayOffThePhone()
    {
        var forbidden = new[] { "System.Net", "HttpClient", "Process.Start", "Clipboard", "Console.Write", "Debug.Write", "ILogger", "IBodyLog" };
        var files = Directory.EnumerateFiles(Path.Combine(ConstitutionSource.Root(), "Constitution.Domain", "Workouts"), "*.cs")
            .Where(file => Path.GetFileName(file).StartsWith("Recovery", StringComparison.Ordinal)
                || Path.GetFileName(file).StartsWith("Soreness", StringComparison.Ordinal)
                || Path.GetFileName(file).StartsWith("Progression", StringComparison.Ordinal))
            .ToList();

        Assert.True(files.Count >= 9, "The scan found too few files to mean anything.");
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var word in forbidden)
            {
                Assert.False(text.Contains(word, StringComparison.Ordinal), $"{Path.GetFileName(file)} contains '{word}'.");
            }
        }
    }
}
