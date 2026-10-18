using Constitution.Domain.Body;

namespace Constitution.Tests.Body;

/// <summary>C-7.1.2 — readings are taken from install day, only for signals the platform says are granted.</summary>
public class SignalReaderTests
{
    private static SignalReader ReaderFor(
        MemoryBodyStore store, FakeHealthSource source, DateOnly today, CapturingBodyLog? log = null, ISleepGapFiller? filler = null) =>
        new(store, source, () => today, log ?? new CapturingBodyLog(), filler);

    [Fact]
    public async Task StartsOnInstallDay()
    {
        var store = new MemoryBodyStore();
        var source = new FakeHealthSource().Grant(BodySignal.Weight);

        await ReaderFor(store, source, Samples.Today).ReadAsync(default);

        Assert.Equal(Samples.Today, store.InstallDay);
        var asked = Assert.Single(source.Asked);
        Assert.Equal((BodySignal.Weight, Samples.Today, Samples.Today), asked);
    }

    [Fact]
    public async Task NeverAsksForADayBeforeInstall()
    {
        var store = new MemoryBodyStore();
        store.SetInstallDay(Samples.Today.AddDays(-3));
        var source = new FakeHealthSource().Grant(BodySignal.Steps).Grant(BodySignal.Weight);

        await ReaderFor(store, source, Samples.Today).ReadAsync(default);

        Assert.All(source.Asked, ask => Assert.Equal(Samples.Today.AddDays(-3), ask.From));
        Assert.All(source.Asked, ask => Assert.Equal(Samples.Today, ask.To));
    }

    [Fact]
    public async Task DropsAnythingDatedBeforeInstallAnyway()
    {
        var store = new MemoryBodyStore();
        store.SetInstallDay(Samples.Today.AddDays(-1));
        var source = new FakeHealthSource().Grant(
            BodySignal.Weight,
            Samples.Of(Samples.Today.AddDays(-5), 190),
            Samples.Of(Samples.Today.AddDays(-1), 181),
            Samples.Of(Samples.Today, 180));

        await ReaderFor(store, source, Samples.Today).ReadAsync(default);

        Assert.Equal([Samples.Today.AddDays(-1), Samples.Today], store.All().Select(reading => reading.Day));
    }

    [Fact]
    public async Task ReadsOnlyGrantedSignals()
    {
        var store = new MemoryBodyStore();
        var source = new FakeHealthSource().Grant(BodySignal.Weight, Samples.Of(Samples.Today, 180));
        source.Granted.Remove(BodySignal.Sleep);

        var report = await ReaderFor(store, source, Samples.Today).ReadAsync(default);

        Assert.Equal([BodySignal.Weight], source.Asked.Select(ask => ask.Signal));
        Assert.Equal(SignalReadStatus.Read, report.StatusOf(BodySignal.Weight));
        Assert.Equal(SignalReadStatus.NotGranted, report.StatusOf(BodySignal.Sleep));
        Assert.Equal(6, report.Count(SignalReadStatus.NotGranted));
        Assert.All(store.All(), reading => Assert.Equal(BodySignal.Weight, reading.Signal));
    }

    [Fact]
    public async Task ASignalNeverGrantedIsNeverTouched()
    {
        var store = new MemoryBodyStore();
        var source = new FakeHealthSource();

        var report = await ReaderFor(store, source, Samples.Today).ReadAsync(default);

        Assert.Empty(source.Asked);
        Assert.True(store.IsEmpty);
        Assert.Equal(0, report.Count(SignalReadStatus.Read));
    }

    [Fact]
    public async Task ResumesFromTheLastDayItStored()
    {
        var store = new MemoryBodyStore();
        store.SetInstallDay(Samples.Today.AddDays(-10));
        store.Upsert([new BodyReading(BodySignal.Weight, Samples.Today.AddDays(-4), 181)]);
        var source = new FakeHealthSource().Grant(BodySignal.Weight);

        await ReaderFor(store, source, Samples.Today).ReadAsync(default);

        // The last stored day is read again: a day that was still filling in when it was last read.
        Assert.Equal(Samples.Today.AddDays(-4), Assert.Single(source.Asked).From);
    }

    [Fact]
    public async Task NothingBeforeThisPassesRangeIsRewritten()
    {
        // A night that started before the range may come back as a partial reading of the day before it.
        var store = new MemoryBodyStore();
        store.SetInstallDay(Samples.Today.AddDays(-10));
        store.Upsert([new BodyReading(BodySignal.Sleep, Samples.Today.AddDays(-3), 7.5), new BodyReading(BodySignal.Sleep, Samples.Today.AddDays(-2), 6)]);
        var source = new FakeHealthSource().Grant(
            BodySignal.Sleep,
            Samples.Of(Samples.Today.AddDays(-3), 1.5),
            Samples.Of(Samples.Today.AddDays(-2), 6.25));

        await ReaderFor(store, source, Samples.Today).ReadAsync(default);

        var sleep = store.All().ToDictionary(reading => reading.Day, reading => reading.Value);
        Assert.Equal(7.5, sleep[Samples.Today.AddDays(-3)]);
        Assert.Equal(6.25, sleep[Samples.Today.AddDays(-2)]);
    }

    [Fact]
    public async Task AReadingForTheSameDayReplacesTheEarlierOne()
    {
        var store = new MemoryBodyStore();
        var first = new FakeHealthSource().Grant(BodySignal.Steps, Samples.Of(Samples.Today, 3000));
        await ReaderFor(store, first, Samples.Today).ReadAsync(default);

        var later = new FakeHealthSource().Grant(BodySignal.Steps, Samples.Of(Samples.Today, 9000));
        await ReaderFor(store, later, Samples.Today).ReadAsync(default);

        Assert.Equal(9000, Assert.Single(store.All()).Value);
    }

    [Fact]
    public async Task InstallDayIsSetOnceAndNeverMoved()
    {
        var store = new MemoryBodyStore();
        var source = new FakeHealthSource().Grant(BodySignal.Weight);

        await ReaderFor(store, source, Samples.Today).ReadAsync(default);
        await ReaderFor(store, source, Samples.Today.AddDays(5)).ReadAsync(default);

        Assert.Equal(Samples.Today, store.InstallDay);
        Assert.Equal(1, store.SetInstallDayCalls);
    }

    [Theory]
    [InlineData(HealthReadOutcome.Unsupported, SignalReadStatus.Unsupported)]
    [InlineData(HealthReadOutcome.Denied, SignalReadStatus.Denied)]
    [InlineData(HealthReadOutcome.Failed, SignalReadStatus.Failed)]
    public async Task APlatformOutcomeIsReportedNotThrown(HealthReadOutcome outcome, SignalReadStatus expected)
    {
        var store = new MemoryBodyStore();
        var source = new FakeHealthSource().Answer(BodySignal.Steps, new HealthRead(outcome, []));

        var report = await ReaderFor(store, source, Samples.Today).ReadAsync(default);

        Assert.Equal(expected, report.StatusOf(BodySignal.Steps));
        Assert.True(store.IsEmpty);
    }

    [Fact]
    public async Task AFailedSignalDoesNotStopTheOthers()
    {
        var store = new MemoryBodyStore();
        var source = new FakeHealthSource()
            .Answer(BodySignal.Weight, new HealthRead(HealthReadOutcome.Failed, []))
            .Grant(BodySignal.Steps, Samples.Of(Samples.Today, 8000));

        var report = await ReaderFor(store, source, Samples.Today).ReadAsync(default);

        Assert.Equal(SignalReadStatus.Failed, report.StatusOf(BodySignal.Weight));
        Assert.Equal(SignalReadStatus.Read, report.StatusOf(BodySignal.Steps));
        Assert.Equal(8000, Assert.Single(store.All()).Value);
    }

    [Fact]
    public async Task TheBedFillsANightTheWatchMissed()
    {
        var store = new MemoryBodyStore();
        store.SetInstallDay(Samples.Today.AddDays(-1));
        var source = new FakeHealthSource().Grant(BodySignal.Sleep, Samples.Of(Samples.Today, 6.5));
        var filler = new FakeSleepFiller();
        filler.Nights[Samples.Today.AddDays(-1)] = 7.25;
        filler.Nights[Samples.Today] = 9; // the watch has this one, so the filler must not use it

        await ReaderFor(store, source, Samples.Today, filler: filler).ReadAsync(default);

        var sleep = store.All().Where(reading => reading.Signal == BodySignal.Sleep).ToDictionary(reading => reading.Day, reading => reading.Value);
        Assert.Equal(7.25, sleep[Samples.Today.AddDays(-1)]);
        Assert.Equal(6.5, sleep[Samples.Today]);
        Assert.Equal(6.5, filler.Asked.Single(ask => ask.Day == Samples.Today).Watch);
    }

    [Fact]
    public async Task NoSleepGrantMeansTheBedIsNotAsked()
    {
        var store = new MemoryBodyStore();
        var filler = new FakeSleepFiller();
        filler.Nights[Samples.Today] = 7;
        var source = new FakeHealthSource().Grant(BodySignal.Weight);

        await ReaderFor(store, source, Samples.Today, filler: filler).ReadAsync(default);

        Assert.Empty(filler.Asked);
    }

    [Fact]
    public async Task NoFillerMeansTheWatchAlone()
    {
        var store = new MemoryBodyStore();
        var source = new FakeHealthSource().Grant(BodySignal.Sleep);

        await ReaderFor(store, source, Samples.Today, filler: null).ReadAsync(default);

        Assert.True(store.IsEmpty);
    }

    [Fact]
    public async Task TheLogCarriesCountsAndNeverAValue()
    {
        var store = new MemoryBodyStore();
        var log = new CapturingBodyLog();
        var source = new FakeHealthSource().Grant(
            BodySignal.Weight, Samples.Of(Samples.Today, 187.35), Samples.Of(Samples.Today.AddDays(-1), 188.15));
        source.Grant(BodySignal.RestingHeartRate, Samples.Of(Samples.Today, 61.5));

        await ReaderFor(store, source, Samples.Today, log).ReadAsync(default);

        Assert.NotEmpty(log.Lines);
        var text = string.Join("\n", log.Lines);
        foreach (var value in new[] { "187", "188", "61.5", "example.watch" })
        {
            Assert.DoesNotContain(value, text, StringComparison.Ordinal);
        }
        Assert.Contains(log.Lines, line => line.Contains("weight", StringComparison.Ordinal) && line.Contains("1 reading", StringComparison.Ordinal));
    }
}
