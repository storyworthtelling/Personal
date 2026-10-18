using Constitution.Domain.Body;
using Constitution.Infrastructure.Backup;

namespace Constitution.Tests.Backup;

/// <summary>
/// The snapshot the backup copies out of a store and merges before it writes. The merge is what keeps
/// a smaller local history from shrinking a larger backup, so its rules are tested on their own.
/// </summary>
public class BodyHistoryTests
{
    private static readonly DateOnly Day = BackupInvented.FirstDay;

    [Fact]
    public void ASnapshotIsEverythingTheStoreHolds()
    {
        var store = new BackupRig().WithHistory().Store;

        var history = BodyHistory.From(store);

        // The store lists oldest day first; the history lists a signal at a time. Same readings.
        Assert.Equal(27, history.Readings.Count);
        Assert.Equal(store.All().OrderBy(r => r.Signal).ThenBy(r => r.Day), history.Readings);
        Assert.Equal(store.InstallDay, history.InstallDay);
        Assert.False(history.IsEmpty);
    }

    [Fact]
    public void AnEmptyStoreIsAnEmptyHistory()
    {
        var history = BodyHistory.From(new InMemoryBodyStore());

        Assert.True(history.IsEmpty);
        Assert.Null(history.InstallDay);
        Assert.True(history.HasSameContentAs(BodyHistory.Empty));
    }

    [Fact]
    public void ReadingsAreOneAPerSignalPerDayInTheSignalOrder()
    {
        var history = BodyHistory.Create(
            null,
            [
                BackupInvented.One(BodySignal.Sleep, Day.AddDays(1), 7.5),
                BackupInvented.One(BodySignal.Weight, Day.AddDays(1), 181),
                BackupInvented.One(BodySignal.Weight, Day, 180),
                BackupInvented.One(BodySignal.Weight, Day, 179),
            ]);

        Assert.Equal(
            [
                BackupInvented.One(BodySignal.Weight, Day, 179),
                BackupInvented.One(BodySignal.Weight, Day.AddDays(1), 181),
                BackupInvented.One(BodySignal.Sleep, Day.AddDays(1), 7.5),
            ],
            history.Readings);
    }

    [Fact]
    public void MergingKeepsEveryReadingFromBothSides()
    {
        var remote = BodyHistory.Create(null, [BackupInvented.One(BodySignal.Weight, Day, 180), BackupInvented.One(BodySignal.Steps, Day, 4000)]);
        var local = BodyHistory.Create(null, [BackupInvented.One(BodySignal.Weight, Day.AddDays(1), 181)]);

        var merged = remote.MergedWith(local);

        Assert.Equal(3, merged.Readings.Count);
        Assert.Contains(BackupInvented.One(BodySignal.Steps, Day, 4000), merged.Readings);
        Assert.Contains(BackupInvented.One(BodySignal.Weight, Day.AddDays(1), 181), merged.Readings);
    }

    [Fact]
    public void OnADayBothHoldTheNewerSidesReadingWins()
    {
        var remote = BodyHistory.Create(null, [BackupInvented.One(BodySignal.Weight, Day, 180)]);
        var local = BodyHistory.Create(null, [BackupInvented.One(BodySignal.Weight, Day, 179.5)]);

        Assert.Equal(179.5, Assert.Single(remote.MergedWith(local).Readings).Value);
        Assert.Equal(180, Assert.Single(local.MergedWith(remote).Readings).Value);
    }

    [Fact]
    public void MergingTakesTheEarlierInstallDay()
    {
        var early = BodyHistory.Create(new DateOnly(2026, 1, 5), [BackupInvented.One(BodySignal.Steps, Day, 1)]);
        var late = BodyHistory.Create(new DateOnly(2026, 11, 1), [BackupInvented.One(BodySignal.Steps, Day, 2)]);
        var none = BodyHistory.Create(null, [BackupInvented.One(BodySignal.Steps, Day, 3)]);

        Assert.Equal(new DateOnly(2026, 1, 5), early.MergedWith(late).InstallDay);
        Assert.Equal(new DateOnly(2026, 1, 5), late.MergedWith(early).InstallDay);
        Assert.Equal(new DateOnly(2026, 1, 5), early.MergedWith(none).InstallDay);
        Assert.Equal(new DateOnly(2026, 1, 5), none.MergedWith(early).InstallDay);
        Assert.Null(none.MergedWith(none).InstallDay);
    }

    [Fact]
    public void MergingNothingChangesNothing()
    {
        var history = BodyHistory.Create(BackupInvented.InstallDay, BackupInvented.EverySignal());

        Assert.True(history.HasSameContentAs(history.MergedWith(BodyHistory.Empty)));
        Assert.True(history.HasSameContentAs(BodyHistory.Empty.MergedWith(history)));
    }

    [Fact]
    public void ContentIsSameOnlyWhenReadingsAndInstallDayAgree()
    {
        var readings = BackupInvented.EverySignal();
        var a = BodyHistory.Create(BackupInvented.InstallDay, readings);

        Assert.True(a.HasSameContentAs(BodyHistory.Create(BackupInvented.InstallDay, readings.AsEnumerable().Reverse())));
        Assert.False(a.HasSameContentAs(BodyHistory.Create(BackupInvented.InstallDay.AddDays(1), readings)));
        Assert.False(a.HasSameContentAs(BodyHistory.Create(BackupInvented.InstallDay, readings.Skip(1))));
        Assert.False(a.HasSameContentAs(BodyHistory.Create(
            BackupInvented.InstallDay,
            readings.Select((reading, index) => index == 0 ? reading with { Value = reading.Value + 1 } : reading))));
    }

    [Fact]
    public void AReadingThatIsNotANumberIsLeftOut()
    {
        var history = BodyHistory.Create(
            null,
            [BackupInvented.One(BodySignal.Weight, Day, double.NaN), BackupInvented.One(BodySignal.Weight, Day.AddDays(1), double.NegativeInfinity)]);

        Assert.True(history.IsEmpty);
    }
}
