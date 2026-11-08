using Codex.Domain.Audio;
using Codex.Infrastructure.Podcasts;
using Codex.Tests.Support;
using Xunit;

namespace Codex.Tests.Audio;

// A garbled backup read as "no backup yet", which the reconcile then backed up over. The read now tells the
// three apart, so a damaged file is left for the owner rather than replaced (G14).
public class DriveAudioLibraryStoreReadTests
{
    [Fact]
    public async Task NoFileIsAbsentAndReadable()
    {
        var read = await new DriveAudioLibraryStore(new InMemoryDriveTextFile()).ReadAsync(default);

        Assert.True(read.Readable);
        Assert.Null(read.Snapshot);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("   ")]
    public async Task ADamagedFileIsUnreadableNotAbsent(string content)
    {
        var read = await new DriveAudioLibraryStore(new InMemoryDriveTextFile { Content = content }).ReadAsync(default);

        Assert.False(read.Readable);
    }

    [Fact]
    public async Task ABackupIsRead()
    {
        var drive = new InMemoryDriveTextFile();
        var store = new DriveAudioLibraryStore(drive);
        await store.SaveAsync(new AudioLibrarySnapshot([new PodcastSubscription("https://a.example/feed", "A", DateTimeOffset.UnixEpoch, 0)], [], []), default);

        var read = await store.ReadAsync(default);

        Assert.True(read.Readable);
        Assert.Single(read.Snapshot!.Subscriptions);
    }
}
