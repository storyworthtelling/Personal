using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

// Error-handling audit §2 (critical). The archive backup mirrored the phone: whatever the phone held replaced
// what Drive held. A restore that never ran (mail typed in by hand after a reset, or offline at first launch)
// left the phone with nothing filed, and the first message filed then replaced the only copy of every filed
// message with one ghost. The backup is now a union keyed by SourceId, merged in both directions on every sync,
// and an unfiled message stays unfiled because the snapshot remembers when it was taken back out.
public class ArchiveMergeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    private static Ghost Filed(string id, int minutesAgo = 10) =>
        new(new TimelineItem(TimelineItemKind.Email, "sam@example.com", Now.AddDays(-1), "invented " + id, true, id), Now.AddMinutes(-minutesAgo));

    private static ArchiveSnapshot Snapshot(params Ghost[] ghosts) => new(ghosts, Now);

    private static IReadOnlyList<string> Ids(ArchiveSnapshot snapshot) =>
        snapshot.Ghosts.Select(g => g.Item.SourceId).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public void ThePhonesOneNewGhostIsAddedToTheBackupNotSwappedForIt()
    {
        var backedUp = Snapshot(Filed("m1"), Filed("m2"), Filed("m3"));
        var phone = Snapshot(Filed("m9", minutesAgo: 1));

        var merged = ArchiveSync.Merge(phone, backedUp);

        Assert.Equal(["m1", "m2", "m3", "m9"], Ids(merged));
        Assert.True(ArchiveSync.NeedsWriting(merged, backedUp));
    }

    [Fact]
    public void WhatTheBackupHoldsAndThePhoneDoesNotIsRestored()
    {
        var backedUp = Snapshot(Filed("m1"), Filed("m2"));
        var phone = Snapshot(Filed("m2"));

        var merged = ArchiveSync.Merge(phone, backedUp);

        Assert.Equal(["m1"], ArchiveSync.ToRestore(merged, phone).Select(g => g.Item.SourceId));
    }

    [Fact]
    public void AMessageUnfiledOnThePhoneStaysUnfiledAndLeavesTheBackup()
    {
        var backedUp = Snapshot(Filed("m1", minutesAgo: 60), Filed("m2", minutesAgo: 60));
        var phone = Snapshot(Filed("m2", minutesAgo: 60)) with { Unfiled = [new UnfiledGhost("m1", Now.AddMinutes(-5))] };

        var merged = ArchiveSync.Merge(phone, backedUp);

        Assert.Equal(["m2"], Ids(merged));
        Assert.Empty(ArchiveSync.ToRestore(merged, phone));
        Assert.Contains(merged.Unfiled, u => u.SourceId == "m1");
    }

    [Fact]
    public void AMessageFiledAgainAfterItWasUnfiledIsKept()
    {
        var backedUp = Snapshot() with { Unfiled = [new UnfiledGhost("m1", Now.AddMinutes(-30))] };
        var phone = Snapshot(Filed("m1", minutesAgo: 2));

        var merged = ArchiveSync.Merge(phone, backedUp);

        Assert.Equal(["m1"], Ids(merged));
    }

    [Fact]
    public void AnUnchangedArchiveIsNotWrittenAndNothingIsNeverWritten()
    {
        var backedUp = Snapshot(Filed("m1"));

        Assert.False(ArchiveSync.NeedsWriting(ArchiveSync.Merge(Snapshot(Filed("m1")), backedUp), backedUp));
        Assert.False(ArchiveSync.NeedsWriting(ArchiveSync.Merge(Snapshot(), null), null));
    }

    [Fact]
    public void ANewUnfiledEntryIsWorthWriting()
    {
        var backedUp = Snapshot(Filed("m1"), Filed("m2"));
        var phone = Snapshot(Filed("m2")) with { Unfiled = [new UnfiledGhost("m1", Now)] };

        Assert.True(ArchiveSync.NeedsWriting(ArchiveSync.Merge(phone, backedUp), backedUp));
    }

    [Fact]
    public void TheUnfiledListRoundTripsAndAnOldBackupHasNone()
    {
        var snapshot = Snapshot(Filed("m2")) with { Unfiled = [new UnfiledGhost("m1", Now)] };

        var decoded = ArchiveSnapshotCodec.Decode(ArchiveSnapshotCodec.Encode(snapshot))!;
        var old = ArchiveSnapshotCodec.Decode(System.Text.Json.JsonSerializer.Serialize(new { Ghosts = Array.Empty<object>(), TakenUtc = Now }))!;

        Assert.Equal("m1", Assert.Single(decoded.Unfiled).SourceId);
        Assert.Empty(old.Unfiled);
    }
}
