using Storyworthtelling.Voice;
using Xunit;

namespace Storyworthtelling.Voice.Tests;

public class InMemoryDriveFileIdStoreTests
{
    [Fact]
    public void GetReturnsNullUntilSet()
    {
        var store = new InMemoryDriveFileIdStore();
        Assert.Null(store.Get("voice.json"));

        store.Set("voice.json", "abc123");
        Assert.Equal("abc123", store.Get("voice.json"));
    }

    [Fact]
    public void SetOverwritesAPreviousId()
    {
        var store = new InMemoryDriveFileIdStore();
        store.Set("voice.json", "old");
        store.Set("voice.json", "new");
        Assert.Equal("new", store.Get("voice.json"));
    }

    [Fact]
    public void SeedsFromAnExistingMapAndSnapshotsBack()
    {
        var store = new InMemoryDriveFileIdStore(new Dictionary<string, string> { ["a.json"] = "id-a" });
        store.Set("b.json", "id-b");

        var snapshot = store.Snapshot();
        Assert.Equal("id-a", snapshot["a.json"]);
        Assert.Equal("id-b", snapshot["b.json"]);

        // Snapshot is a copy — mutating the store afterwards doesn't change a taken snapshot.
        store.Set("c.json", "id-c");
        Assert.False(snapshot.ContainsKey("c.json"));
    }

    [Fact]
    public void OnChange_FiresOnNewOrChangedId_NotOnANoOpSet()
    {
        var snapshots = new List<IReadOnlyDictionary<string, string>>();
        var store = new InMemoryDriveFileIdStore(onChange: snapshots.Add);

        store.Set("a.json", "id-a"); // new -> fires
        store.Set("a.json", "id-a"); // unchanged -> no fire
        store.Set("a.json", "id-a2"); // changed -> fires

        Assert.Equal(2, snapshots.Count);
        Assert.Equal("id-a2", snapshots[^1]["a.json"]);
    }

    [Fact]
    public void MapCodec_RoundTrips_AndDecodesAbsentOrGarbledToEmpty()
    {
        var map = new Dictionary<string, string> { ["voice.json"] = "id-1", ["vault.json"] = "id-2" };
        var decoded = DriveFileIdMapCodec.Decode(DriveFileIdMapCodec.Encode(map));

        Assert.Equal("id-1", decoded["voice.json"]);
        Assert.Equal("id-2", decoded["vault.json"]);
        Assert.Empty(DriveFileIdMapCodec.Decode(null));
        Assert.Empty(DriveFileIdMapCodec.Decode("{ not json"));
    }
}
