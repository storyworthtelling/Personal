using Pulp.Domain.Household;
using Pulp.Infrastructure.Household;

namespace Pulp.Tests.Infrastructure.Household;

/// <summary>
/// The phone's own copy of the household, in its data directory. Written by temp file and replace,
/// so a write cut off half way — the app killed, the battery gone — leaves the previous household
/// whole rather than half a file that the next launch refuses.
/// </summary>
public sealed class LocalHouseholdStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pulp-household-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static HouseholdDocument One(string id, long wall) =>
        HouseholdDocument.Empty.With(HouseholdRecord.Write(
            new RecordKey("viewer", id),
            1,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["child"] = "false" },
            new HlcTimestamp(wall, 0),
            "install-a-example"));

    [Fact]
    public async Task ANewPhoneHasAnEmptyHousehold()
    {
        var store = new LocalHouseholdStore(directory);

        Assert.Empty((await store.LoadAsync(CancellationToken.None)).Records);
    }

    [Fact]
    public async Task WhatIsSavedIsLoaded()
    {
        var store = new LocalHouseholdStore(directory);
        await store.SaveAsync(One("robin-example", 10), CancellationToken.None);
        await store.SaveAsync(One("avery-example", 20), CancellationToken.None);

        var loaded = await new LocalHouseholdStore(directory).LoadAsync(CancellationToken.None);

        Assert.True(loaded.ContentEquals(One("avery-example", 20)));
    }

    [Fact]
    public async Task AWriteLeavesNoTemporaryFileBehind()
    {
        var store = new LocalHouseholdStore(directory);
        await store.SaveAsync(One("robin-example", 10), CancellationToken.None);

        Assert.Equal([LocalHouseholdStore.FileName], Directory.GetFiles(directory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task AnInterruptedWriteLeavesTheLastHouseholdWhole()
    {
        var store = new LocalHouseholdStore(directory);
        await store.SaveAsync(One("robin-example", 10), CancellationToken.None);

        // A crash after the temp file was half written and before it replaced the real one.
        await File.WriteAllTextAsync(Path.Combine(directory, LocalHouseholdStore.FileName + ".tmp"), "{\"format\":1,\"rec");

        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.True(loaded.ContentEquals(One("robin-example", 10)));

        await store.SaveAsync(One("avery-example", 20), CancellationToken.None);
        Assert.True((await store.LoadAsync(CancellationToken.None)).ContentEquals(One("avery-example", 20)));
    }

    [Fact]
    public async Task ADamagedFileIsRefusedNotReadAsEmpty()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, LocalHouseholdStore.FileName), "{\"format\":1,\"rec");

        // Read as empty, the next save would write an empty household over the damaged one for good.
        await Assert.ThrowsAsync<HouseholdFormatException>(() => new LocalHouseholdStore(directory).LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentSavesNeverTearTheFile()
    {
        var store = new LocalHouseholdStore(directory);

        await Task.WhenAll(Enumerable.Range(1, 40).Select(i => Task.Run(() => store.SaveAsync(One("viewer-" + i, i), CancellationToken.None))));

        Assert.Single((await store.LoadAsync(CancellationToken.None)).Records);
    }

    [Fact]
    public void TheStoreIsTheSeamDriveWillImplementToo() =>
        Assert.IsAssignableFrom<IHouseholdStore>(new LocalHouseholdStore(directory));
}
