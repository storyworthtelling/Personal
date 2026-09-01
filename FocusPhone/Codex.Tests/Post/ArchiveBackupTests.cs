using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// The ghost archive is the only copy of a filed message: the original was deleted at the
/// source on purpose. It survives a reboot in timeline.db — and would not survive the
/// factory reset the cutover plans for, which is what the Drive backup is for.
///
/// One writer, like the audio library: the phone. So this is a mirror, not a merge.
/// </summary>
public class ArchiveBackupTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);

    private static Ghost Filed(string id) =>
        new(new TimelineItem(TimelineItemKind.Email, "her@example.com", Now, "Seven?", true, id)
        {
            Subject = "Dinner",
        }, Now);

    [Fact]
    public void ASnapshotRoundTrips()
    {
        var snapshot = new ArchiveSnapshot([Filed("m1"), Filed("m2")], Now);

        var back = ArchiveSnapshotCodec.Decode(ArchiveSnapshotCodec.Encode(snapshot));

        Assert.Equal(2, back!.Ghosts.Count);
        Assert.Equal("Dinner", back.Ghosts[0].Item.Subject);
    }

    // An unchanged archive must not be written. Drive writes cost battery and quota, and
    // this runs on every sync.
    [Fact]
    public void AnUnchangedArchiveIsNotWorthWriting()
    {
        var snapshot = new ArchiveSnapshot([Filed("m1")], Now);
        var same = new ArchiveSnapshot([Filed("m1")], Now.AddHours(3));

        Assert.False(ArchiveSync.NeedsWriting(same, snapshot));
    }

    [Fact]
    public void AChangedArchiveIsWritten()
    {
        var snapshot = new ArchiveSnapshot([Filed("m1")], Now);
        var grown = new ArchiveSnapshot([Filed("m1"), Filed("m2")], Now);

        Assert.True(ArchiveSync.NeedsWriting(grown, snapshot));
    }

    // Nothing backed up yet, and something to back up: write it.
    [Fact]
    public void AFirstBackupIsAlwaysWritten()
    {
        Assert.True(ArchiveSync.NeedsWriting(new ArchiveSnapshot([Filed("m1")], Now), null));
    }

    // An EMPTY local archive must never overwrite a full backup. A fresh install has
    // nothing filed yet, and writing that up would destroy the only copy of every filed
    // message the owner has — which is the whole thing this exists to protect.
    [Fact]
    public void AnEmptyArchiveNeverOverwritesABackup()
    {
        var backed = new ArchiveSnapshot([Filed("m1"), Filed("m2")], Now);

        Assert.False(ArchiveSync.NeedsWriting(new ArchiveSnapshot([], Now), backed));
    }

    // The other direction of the same rule: a device with nothing filed takes what is in
    // Drive, and a device that already has ghosts is the authority on its own archive.
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void OnlyAnEmptyDeviceRestores(int localGhosts, bool expectRestore)
    {
        var local = Enumerable.Range(0, localGhosts).Select(i => Filed($"local{i}")).ToList();

        Assert.Equal(expectRestore, ArchiveSync.ShouldRestore(local));
    }
}
