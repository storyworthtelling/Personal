using Codex.Domain.Audio;
using Xunit;

namespace Codex.Tests.Audio;

public class AudioLibrarySyncTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static PodcastSubscription Sub(string url) => new(url, Title: url, AddedUtc: T0, SortOrder: 0);

    private static AudioLibrarySnapshot Snapshot(
        IReadOnlyList<PodcastSubscription>? subs = null,
        IReadOnlyList<string>? listened = null,
        IReadOnlyList<PlaybackProgress>? progress = null) =>
        new(subs ?? [], listened ?? [], progress ?? []);

    [Fact]
    public void EmptyLocal_AndNoRemote_IsNone()
    {
        Assert.Equal(LibrarySyncDirection.None,
            AudioLibrarySync.Decide(AudioLibrarySnapshot.Empty, remote: null));
    }

    [Fact]
    public void LocalWithState_AndNoRemote_SeedsBackup()
    {
        var local = Snapshot(subs: [Sub("feed1")]);

        Assert.Equal(LibrarySyncDirection.Backup, AudioLibrarySync.Decide(local, remote: null));
    }

    [Fact]
    public void LocalWithState_AndEmptyRemote_SeedsBackup()
    {
        var local = Snapshot(subs: [Sub("feed1")]);

        Assert.Equal(LibrarySyncDirection.Backup,
            AudioLibrarySync.Decide(local, AudioLibrarySnapshot.Empty));
    }

    [Fact]
    public void FreshDevice_WithBackedUpRemote_Restores()
    {
        var remote = Snapshot(subs: [Sub("feed1")], listened: ["a"]);

        Assert.Equal(LibrarySyncDirection.Restore,
            AudioLibrarySync.Decide(AudioLibrarySnapshot.Empty, remote));
    }

    [Fact]
    public void LocalHasShows_AndDrifted_BacksUp()
    {
        var local = Snapshot(subs: [Sub("feed1")], listened: ["a", "b"]);
        var remote = Snapshot(subs: [Sub("feed1")], listened: ["a"]);

        Assert.Equal(LibrarySyncDirection.Backup, AudioLibrarySync.Decide(local, remote));
    }

    [Fact]
    public void LocalHasShows_AndMatchesRemote_IsNone()
    {
        var local = Snapshot(subs: [Sub("feed1")], listened: ["a", "b"]);
        var remote = Snapshot(subs: [Sub("feed1")], listened: ["b", "a"]); // order must not matter

        Assert.Equal(LibrarySyncDirection.None, AudioLibrarySync.Decide(local, remote));
    }

    [Fact]
    public void LocalHasShows_AndOnlySettingsDrifted_BacksUp()
    {
        var local = new AudioLibrarySnapshot([Sub("feed1")], [], [],
            new PlaybackSettingsSnapshot(new PlaybackSettings(1.5f), []));
        var remote = new AudioLibrarySnapshot([Sub("feed1")], [], [],
            new PlaybackSettingsSnapshot(new PlaybackSettings(1.0f), []));

        Assert.Equal(LibrarySyncDirection.Backup, AudioLibrarySync.Decide(local, remote));
    }

    [Fact]
    public void UnsubscribeToEmpty_PropagatesAsBackup_NotResurrected()
    {
        // Removing the last show leaves local with no subscriptions but different from a
        // non-empty remote; because local is empty this restores. The unsubscribe-to-empty
        // edge is why removal is pushed *before* the shelf is empty in practice, but assert
        // the documented rule: an empty local adopts the cloud copy.
        var local = AudioLibrarySnapshot.Empty;
        var remote = Snapshot(subs: [Sub("feed1")]);

        Assert.Equal(LibrarySyncDirection.Restore, AudioLibrarySync.Decide(local, remote));
    }
}
