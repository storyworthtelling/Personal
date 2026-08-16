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
}
