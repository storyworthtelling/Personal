using Codex.Domain.Audio;
using Xunit;

namespace Codex.Tests.Audio;

// Error-handling §17: the same overwrite as Post's archive. If the restore failed at first launch (offline, or
// not signed in yet), the phone's first new subscription backed up over the whole library: shows, listened
// episodes, resume positions and EQ. Until this install has once read Drive, a reconcile now merges both ways;
// after that the phone stays the authority, so an unsubscribe still propagates rather than being brought back.
public class AudioLibraryMergeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static PodcastSubscription Sub(string url) => new(url, Title: url, AddedUtc: T0, SortOrder: 0);

    private static AudioLibrarySnapshot Library(string[] subs, string[]? listened = null) =>
        new(subs.Select(Sub).ToList(), listened ?? [], []);

    [Fact]
    public void BeforeTheFirstConfirmedReadAPhoneWithOneShowMergesInsteadOfOverwriting()
    {
        var remote = Library(["https://a.example/feed", "https://b.example/feed"], ["ep1", "ep2"]);
        var local = Library(["https://c.example/feed"]);

        Assert.Equal(LibrarySyncDirection.Merge, AudioLibrarySync.Decide(local, remote, restoreConfirmed: false));

        var merged = AudioLibrarySync.Union(local, remote);
        Assert.Equal(3, merged.Subscriptions.Count);
        Assert.Equal(["ep1", "ep2"], merged.ListenedEpisodeIds.Order());
    }

    [Fact]
    public void AfterItThePhoneIsTheAuthoritySoAnUnsubscribePropagates()
    {
        var remote = Library(["https://a.example/feed", "https://b.example/feed"]);
        var local = Library(["https://a.example/feed"]);

        Assert.Equal(LibrarySyncDirection.Backup, AudioLibrarySync.Decide(local, remote, restoreConfirmed: true));
    }

    [Fact]
    public void AnEmptyPhoneStillRestoresAndNothingInDriveStillBacksUp()
    {
        var remote = Library(["https://a.example/feed"]);

        Assert.Equal(LibrarySyncDirection.Restore, AudioLibrarySync.Decide(AudioLibrarySnapshot.Empty, remote, restoreConfirmed: false));
        Assert.Equal(LibrarySyncDirection.Backup, AudioLibrarySync.Decide(remote, null, restoreConfirmed: false));
    }

    [Fact]
    public void TheMergeKeepsThePhonesCopyOfAShowBothHold()
    {
        var local = new AudioLibrarySnapshot([Sub("https://a.example/feed") with { Title = "Phone title" }], [], []);
        var remote = new AudioLibrarySnapshot([Sub("https://a.example/feed") with { Title = "Drive title" }], [], []);

        Assert.Equal("Phone title", Assert.Single(AudioLibrarySync.Union(local, remote).Subscriptions).Title);
    }
}
