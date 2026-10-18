using System.Net;
using Constitution.Infrastructure.SleepNumber;

namespace Constitution.Tests.SleepNumber;

/// <summary>C-7.2.1 — the bed stands in only on a night the watch read zero.</summary>
public class WatchOrBedTests
{
    private static readonly DateOnly Morning = new(2026, 10, 17);
    private static readonly BedNight SevenHours = new(Morning, TimeSpan.FromHours(7));

    [Theory]
    [InlineData(6.5)]
    [InlineData(0.25)]
    public void AWatchReadingAlwaysWins(double watch)
    {
        var chosen = WatchOrBed.Choose(watch, SevenHours);

        Assert.Equal(new SleepReading(watch, SleepSource.Watch), chosen);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(null)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void ABedNightFillsAZeroOrMissingWatch(double? watch)
    {
        var chosen = WatchOrBed.Choose(watch, SevenHours);

        Assert.Equal(new SleepReading(7, SleepSource.Bed), chosen);
    }

    [Fact]
    public void NothingFromEitherIsNoReading()
    {
        Assert.Null(WatchOrBed.Choose(0, null));
        Assert.Null(WatchOrBed.Choose(null, null));
    }

    [Fact]
    public void AWatchReadingNeedsNoBedAtAll()
    {
        Assert.Equal(new SleepReading(8, SleepSource.Watch), WatchOrBed.Choose(8, null));
    }

    [Fact]
    public async Task OnlyWhenTheWatchReadsZero()
    {
        var bed = Bed.Working().On("sleepData", Bed.Reply(HttpStatusCode.OK, Bed.SleepData("2026-10-17", 25200)));
        var supplier = new BedSleepSupplier(Bed.Client(bed, new CapturingSleepLog()), new FixedChoice(BedSide.Left));

        var worn = await supplier.SleepAsync(Morning, 6.5, default);
        Assert.Equal(new SleepReading(6.5, SleepSource.Watch), worn.Value);
        Assert.Empty(bed.Sent);

        var zero = await supplier.SleepAsync(Morning, 0, default);
        Assert.Equal(new SleepReading(7, SleepSource.Bed), zero.Value);
        Assert.Equal(1, bed.CallsTo("sleepData"));
    }

    [Fact]
    public async Task ABedWithNoReadingLeavesTheNightEmpty()
    {
        var bed = Bed.Working().On("sleepData", Bed.Reply(HttpStatusCode.OK, Bed.SleepData("2026-10-17", 0)));
        var supplier = new BedSleepSupplier(Bed.Client(bed, new CapturingSleepLog()), new FixedChoice(BedSide.Left));

        var result = await supplier.SleepAsync(Morning, 0, default);

        Assert.True(result.IsOk);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task NoChosenSideIsNoSuchSide()
    {
        var bed = Bed.Working();
        var supplier = new BedSleepSupplier(Bed.Client(bed, new CapturingSleepLog()), new FixedChoice(null));

        var result = await supplier.SleepAsync(Morning, 0, default);

        Assert.Equal(SleepIqFailure.NoSuchSide, result.Failure);
        Assert.Empty(bed.Sent);
    }

    [Fact]
    public async Task AnOfflineBedIsPassedOnNotHidden()
    {
        var bed = Bed.Working().On("login", Bed.Reply(HttpStatusCode.BadGateway));
        var supplier = new BedSleepSupplier(Bed.Client(bed, new CapturingSleepLog()), new FixedChoice(BedSide.Right));

        var result = await supplier.SleepAsync(Morning, 0, default);

        Assert.Equal(SleepIqFailure.Offline, result.Failure);
    }
}
