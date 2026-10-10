using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Infrastructure.Household;

namespace Pulp.Tests.Infrastructure.Household;

/// <summary>
/// What belongs to this install and not the household: the writer id stamped on every record, and the
/// hybrid logical clock's last stamp. A writer id that changed between launches would make one phone
/// two writers; a clock that restarted from zero on a phone whose time was set back would stamp
/// edits older than its own last ones, which then lose to them and vanish.
/// </summary>
public sealed class LocalInstallStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pulp-install-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheInstallIdIsMadeOnceAndKept()
    {
        var first = new LocalInstallStore(directory).Load();
        var second = new LocalInstallStore(directory).Load();

        Assert.False(string.IsNullOrWhiteSpace(first.InstallId));
        Assert.Equal(first.InstallId, second.InstallId);
        Assert.Equal(HlcTimestamp.Zero, first.LastStamp);
    }

    [Fact]
    public void TheClockResumesFromItsLastStamp()
    {
        var store = new LocalInstallStore(directory);
        var id = store.Load().InstallId;
        store.SaveClock(new HlcTimestamp(1_760_000_000_000, 3));

        var back = new LocalInstallStore(directory).Load();

        Assert.Equal(id, back.InstallId);
        Assert.Equal(new HlcTimestamp(1_760_000_000_000, 3), back.LastStamp);
    }

    [Fact]
    public void TheClockNeverStepsBackOnDisk()
    {
        var store = new LocalInstallStore(directory);
        store.SaveClock(new HlcTimestamp(900, 0));
        store.SaveClock(new HlcTimestamp(800, 5));

        Assert.Equal(new HlcTimestamp(900, 0), store.Load().LastStamp);
    }

    [Fact]
    public void ADamagedInstallFileIsRefusedRatherThanReplacedWithANewWriter()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, LocalInstallStore.FileName), "{ broken");

        Assert.Throws<HouseholdFormatException>(() => new LocalInstallStore(directory).Load());
    }

    [Fact]
    public void ANewInstallIsMatchedToNobody() => Assert.Null(new LocalInstallStore(directory).Load().Viewer);

    /// <summary>
    /// Whose phone this is belongs to the install, not the household: Release D's first run records it
    /// here, and it must survive the clock being saved after every household edit.
    /// </summary>
    [Fact]
    public void TheViewerIsKeptAcrossLaunchesAndClockSaves()
    {
        var store = new LocalInstallStore(directory);
        var id = store.Load().InstallId;
        store.SaveViewer(new ViewerId("ash-example"));
        store.SaveClock(new HlcTimestamp(1_760_000_000_000, 1));

        var back = new LocalInstallStore(directory).Load();

        Assert.Equal(new ViewerId("ash-example"), back.Viewer);
        Assert.Equal(id, back.InstallId);
        Assert.Equal(new HlcTimestamp(1_760_000_000_000, 1), back.LastStamp);
    }

    [Fact]
    public void SavingTheViewerKeepsTheWriterAndTheClock()
    {
        var store = new LocalInstallStore(directory);
        var id = store.Load().InstallId;
        store.SaveClock(new HlcTimestamp(700, 2));
        store.SaveViewer(new ViewerId("rowan-example"));

        var back = store.Load();

        Assert.Equal(id, back.InstallId);
        Assert.Equal(new HlcTimestamp(700, 2), back.LastStamp);
        Assert.Equal(new ViewerId("rowan-example"), back.Viewer);
    }

    [Fact]
    public void TheViewerCanBeCleared()
    {
        var store = new LocalInstallStore(directory);
        store.SaveViewer(new ViewerId("ash-example"));
        store.SaveViewer(null);

        Assert.Null(new LocalInstallStore(directory).Load().Viewer);
    }

    /// <summary>A file written before the viewer existed still reads, as matched to nobody.</summary>
    [Fact]
    public void AnInstallFileFromBeforeTheViewerStillReads()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, LocalInstallStore.FileName),
            "{\"installId\":\"install-old-example\",\"wall\":500,\"counter\":1}");

        var back = new LocalInstallStore(directory).Load();

        Assert.Equal("install-old-example", back.InstallId);
        Assert.Equal(new HlcTimestamp(500, 1), back.LastStamp);
        Assert.Null(back.Viewer);
    }
}
