using Constitution.Domain.Body;

namespace Constitution.Tests.Body;

/// <summary>
/// Review S1, S6, S12 and N2: how far back a pass reads, how often it asks the bed, and what a failing bed
/// is allowed to take with it.
/// </summary>
public class SignalReaderPassTests
{
    private static SignalReader ReaderFor(
        MemoryBodyStore store, FakeHealthSource source, DateOnly today, CapturingBodyLog? log = null, ISleepGapFiller? filler = null) =>
        new(store, source, () => today, log ?? new CapturingBodyLog(), filler);

    [Fact]
    public async Task AlwaysReadsATrailingWindowAgain()
    {
        var store = new MemoryBodyStore();
        store.SetInstallDay(Samples.Today.AddDays(-20));
        store.Upsert([new BodyReading(BodySignal.Steps, Samples.Today.AddDays(-1), 8000)]);
        var source = new FakeHealthSource().Grant(BodySignal.Steps);

        await ReaderFor(store, source, Samples.Today).ReadAsync(default);

        Assert.Equal(SignalReader.TrailingDays, 3);
        Assert.Equal(Samples.Today.AddDays(-SignalReader.TrailingDays), Assert.Single(source.Asked).From);
    }

    [Fact]
    public async Task LateDataForYesterdayIsPickedUp()
    {
        var store = new MemoryBodyStore();
        store.SetInstallDay(Samples.Today.AddDays(-20));
        store.Upsert([new BodyReading(BodySignal.RestingHeartRate, Samples.Today.AddDays(-1), 58)]);
        var source = new FakeHealthSource().Grant(
            BodySignal.RestingHeartRate, Samples.Of(Samples.Today.AddDays(-2), 57), Samples.Of(Samples.Today.AddDays(-1), 58));

        await ReaderFor(store, source, Samples.Today).ReadAsync(default);

        Assert.Contains(store.All(), reading => reading.Day == Samples.Today.AddDays(-2) && reading.Value == 57);
    }

    [Fact]
    public async Task TheTrailingWindowNeverReachesBeforeInstall()
    {
        var store = new MemoryBodyStore();
        store.SetInstallDay(Samples.Today.AddDays(-1));
        store.Upsert([new BodyReading(BodySignal.Weight, Samples.Today, 180)]);
        var source = new FakeHealthSource().Grant(BodySignal.Weight);

        await ReaderFor(store, source, Samples.Today).ReadAsync(default);

        Assert.Equal(Samples.Today.AddDays(-1), Assert.Single(source.Asked).From);
    }

    [Fact]
    public async Task AClockBehindInstallDayAsksForNothing()
    {
        var store = new MemoryBodyStore();
        store.SetInstallDay(Samples.Today);
        var source = new FakeHealthSource().Grant(BodySignal.Weight).Grant(BodySignal.Steps);

        var report = await ReaderFor(store, source, Samples.Today.AddDays(-1)).ReadAsync(default);

        Assert.Empty(source.Asked);
        Assert.Equal(SignalReadStatus.BeforeInstall, report.StatusOf(BodySignal.Weight));
        Assert.True(store.IsEmpty);
    }

    [Fact]
    public async Task AClockBehindInstallDayLeavesTheBedAlone()
    {
        var store = new MemoryBodyStore();
        store.SetInstallDay(Samples.Today);
        var filler = new FakeSleepFiller();

        await ReaderFor(store, new FakeHealthSource().Grant(BodySignal.Sleep), Samples.Today.AddDays(-1), filler: filler).ReadAsync(default);

        Assert.Empty(filler.Asked);
    }

    [Fact]
    public async Task TheBedIsNotAskedAgainAfterItGivesUp()
    {
        var store = new MemoryBodyStore();
        store.SetInstallDay(Samples.Today.AddDays(-9));
        var filler = new FakeSleepFiller { GiveUpAfterAsks = 1 };
        var source = new FakeHealthSource().Grant(BodySignal.Sleep);

        await ReaderFor(store, source, Samples.Today, filler: filler).ReadAsync(default);

        Assert.Single(filler.Asked);
    }

    [Fact]
    public async Task NightsAskedOfTheBedAreCapped()
    {
        var store = new MemoryBodyStore();
        store.SetInstallDay(Samples.Today.AddDays(-60));
        var filler = new FakeSleepFiller();
        var source = new FakeHealthSource().Grant(BodySignal.Sleep);

        await ReaderFor(store, source, Samples.Today, filler: filler).ReadAsync(default);

        Assert.Equal(SignalReader.BedNightsPerPass, 14);
        Assert.Equal(SignalReader.BedNightsPerPass, filler.Asked.Count);
        Assert.Equal(Samples.Today, filler.Asked.Max(ask => ask.Day));
        Assert.Equal(Samples.Today.AddDays(1 - SignalReader.BedNightsPerPass), filler.Asked.Min(ask => ask.Day));
    }

    [Fact]
    public async Task AThrowingBedCostsTheWatchNothing()
    {
        var store = new MemoryBodyStore();
        store.SetInstallDay(Samples.Today.AddDays(-2));
        var filler = new FakeSleepFiller { ThrowOnAsk = new IOException("planted") };
        var log = new CapturingBodyLog();
        var source = new FakeHealthSource().Grant(BodySignal.Sleep, Samples.Of(Samples.Today, 6.5));

        var report = await ReaderFor(store, source, Samples.Today, log, filler).ReadAsync(default);

        Assert.Equal(6.5, Assert.Single(store.All()).Value);
        Assert.Equal(SignalReadStatus.Read, report.StatusOf(BodySignal.Sleep));
        Assert.Single(filler.Asked);
        Assert.Contains(log.Lines, line => line.Contains("IOException", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, line => line.Contains("planted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACancelledBedStillCancelsThePass()
    {
        var store = new MemoryBodyStore();
        var filler = new FakeSleepFiller { ThrowOnAsk = new OperationCanceledException() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ReaderFor(store, new FakeHealthSource().Grant(BodySignal.Sleep), Samples.Today, filler: filler).ReadAsync(default));
    }
}
