using Constitution.Infrastructure.Body;
using Constitution.Infrastructure.SleepNumber;

namespace Constitution.Tests.Body;

/// <summary>The bed fills a night only when the watch missed it, and an unreachable bed is a quiet null with a phrase in the log.</summary>
public class BedSleepGapFillerTests
{
    private sealed class ScriptedBed : ISleepIqClient
    {
        public int Nights { get; private set; }

        public SleepIqResult<BedNight?> Answer { get; set; } = SleepIqResult<BedNight?>.Ok(new BedNight(Samples.Today, TimeSpan.FromHours(7.25)));

        public Task<SleepIqFailure> SignInAsync(CancellationToken cancellationToken) => Task.FromResult(SleepIqFailure.None);

        public Task<SleepIqResult<IReadOnlyList<BedSleeper>>> SleepersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(SleepIqResult<IReadOnlyList<BedSleeper>>.Ok([]));

        public Task<SleepIqResult<BedNight?>> NightAsync(BedSide side, DateOnly day, CancellationToken cancellationToken)
        {
            Nights++;
            return Task.FromResult(Answer);
        }
    }

    private sealed class Chosen(BedSide? chosen) : ISleeperChoice
    {
        public BedSide? Side => chosen;
    }

    [Fact]
    public async Task TheBedSuppliesANightTheWatchMissed()
    {
        var bed = new ScriptedBed();
        var filler = BedSleepGapFiller.Create(bed, new Chosen(BedSide.Left), new CapturingBodyLog())!;

        Assert.Equal(7.25, await filler.BedHoursAsync(Samples.Today, null, default));
        Assert.Equal(7.25, await filler.BedHoursAsync(Samples.Today, 0, default));
    }

    [Fact]
    public async Task AWatchNightIsNotTheBedsToSupplyAndTheBedIsNotAsked()
    {
        var bed = new ScriptedBed();
        var filler = BedSleepGapFiller.Create(bed, new Chosen(BedSide.Right), new CapturingBodyLog())!;

        Assert.Null(await filler.BedHoursAsync(Samples.Today, 6.5, default));
        Assert.Equal(0, bed.Nights);
    }

    [Fact]
    public async Task ABedWithNoReadingIsNull()
    {
        var bed = new ScriptedBed { Answer = SleepIqResult<BedNight?>.Ok(null) };
        var filler = BedSleepGapFiller.Create(bed, new Chosen(BedSide.Left), new CapturingBodyLog())!;

        Assert.Null(await filler.BedHoursAsync(Samples.Today, null, default));
    }

    [Fact]
    public async Task AnOfflineBedIsNullAndTheLogNamesOnlyTheKind()
    {
        var bed = new ScriptedBed { Answer = SleepIqResult<BedNight?>.Failed(SleepIqFailure.Offline) };
        var log = new CapturingBodyLog();
        var filler = BedSleepGapFiller.Create(bed, new Chosen(BedSide.Left), log)!;

        Assert.Null(await filler.BedHoursAsync(Samples.Today, null, default));
        Assert.Equal("body bed sleep: offline", Assert.Single(log.Lines));
    }

    [Fact]
    public void NoSideChosenMeansNoFillerAtAll()
    {
        Assert.Null(BedSleepGapFiller.Create(new ScriptedBed(), new Chosen(null), new CapturingBodyLog()));
    }
}
