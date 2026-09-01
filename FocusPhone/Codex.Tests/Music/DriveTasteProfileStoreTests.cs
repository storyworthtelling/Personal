using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Tests.Support;
using Xunit;

namespace Codex.Tests.Music;

public class DriveTasteProfileStoreTests
{
    private static TasteProfile Sample() =>
        new("Leans jazz late at night.", ["bebop"], ["albums over singles"], ["hype"],
            ["Jazz"], ["Bill Evans"], ["nights skew mellow"], false, 14);

    [Fact]
    public async Task Load_AbsentFile_ReturnsEmpty()
    {
        var store = new DriveTasteProfileStore(new InMemoryDriveTextFile());
        Assert.True((await store.LoadAsync(CancellationToken.None)).IsEmpty);
    }

    [Fact]
    public async Task Load_BlankFile_ReturnsEmpty()
    {
        var store = new DriveTasteProfileStore(new InMemoryDriveTextFile { Content = "   " });
        Assert.True((await store.LoadAsync(CancellationToken.None)).IsEmpty);
    }

    [Fact]
    public async Task Load_GarbledFile_ReturnsEmpty()
    {
        var store = new DriveTasteProfileStore(new InMemoryDriveTextFile { Content = "{ not json" });
        Assert.True((await store.LoadAsync(CancellationToken.None)).IsEmpty);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTrips()
    {
        var file = new InMemoryDriveTextFile();
        var store = new DriveTasteProfileStore(file);

        await store.SaveAsync(Sample(), CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("Leans jazz late at night.", loaded.Summary);
        Assert.Equal(["Jazz"], loaded.TopGenres);
        Assert.Equal(14, loaded.SessionsAnalyzed);
    }
}
