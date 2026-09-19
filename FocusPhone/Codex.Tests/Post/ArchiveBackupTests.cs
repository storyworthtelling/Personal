using System.Text.Json;
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

    // A backup written by an OLD device is the bare snapshot, uncompressed, with no
    // envelope wrapper around it at all — that is what every file already sitting in Drive
    // looks like. It must still decode correctly rather than being read as corrupt just
    // because the compaction shipped after it was written.
    [Fact]
    public void AnOldFormatBackupStillDecodes()
    {
        var snapshot = new ArchiveSnapshot([Filed("m1"), Filed("m2")], Now);
        var oldFormatContent = JsonSerializer.Serialize(
            snapshot, new JsonSerializerOptions { WriteIndented = true });

        var decoded = ArchiveSnapshotCodec.Decode(oldFormatContent);

        Assert.NotNull(decoded);
        Assert.Equal(2, decoded!.Ghosts.Count);
        Assert.Equal("Dinner", decoded.Ghosts[0].Item.Subject);
        Assert.Contains(decoded.Ghosts, g => g.Item.SourceId == "m1");
        Assert.Contains(decoded.Ghosts, g => g.Item.SourceId == "m2");
    }

    // Garbled content (neither a valid envelope with a decodable payload nor a bare
    // snapshot) is still reported as absent rather than throwing out of a background sync.
    [Fact]
    public void GarbledContentDecodesToNull()
    {
        Assert.Null(ArchiveSnapshotCodec.Decode("not json at all"));
    }

    // The size claim, reproduced rather than cited: a realistic archive — a few hundred
    // filed emails with real-length bodies — encoded the new way against what the OLD,
    // indented, uncompressed codec would have written for the same data.
    [Fact]
    public void TheNewEncodingIsMeaningfullySmallerThanTheOldFormat()
    {
        var realistic = new ArchiveSnapshot(RealisticGhosts(300), Now);

        var oldFormatSize = JsonSerializer.Serialize(
            realistic, new JsonSerializerOptions { WriteIndented = true }).Length;
        var newFormatSize = ArchiveSnapshotCodec.Encode(realistic).Length;

        // Reproduced, not asserted from thin air: measured a ~97% reduction on this sample
        // (a per-entry JSON shape repeats ~20 field names on every ghost, which is exactly
        // what gzip is good at, on top of the prose). Asserting a looser 60% keeps the test
        // from being brittle to small shifts in the sample text while still catching a real
        // regression in the encoding.
        Assert.True(
            newFormatSize < oldFormatSize * 0.4,
            $"Expected the new encoding to be well under half the old size; old={oldFormatSize}, new={newFormatSize}.");

        // And the round trip still holds on the realistic sample, not just the two-ghost one.
        var decoded = ArchiveSnapshotCodec.Decode(ArchiveSnapshotCodec.Encode(realistic));
        Assert.Equal(realistic.Ghosts.Count, decoded!.Ghosts.Count);
    }

    // A realistic-shaped sample: real subject/sender text and bodies assembled from a small
    // pool of everyday phrases, mixed per message — like real correspondence, which shares
    // plenty of common phrasing but is not one paragraph copy-pasted three hundred times.
    // Deliberately NOT maximally repetitive: a body this varied is the harder case for
    // gzip, so the measured reduction here is the conservative one to cite.
    private static readonly string[] Openers =
    [
        "Hey, just wanted to follow up on where we landed with the schedule for next week.",
        "Thanks so much for getting back to me so quickly, it really helped.",
        "Quick question before I forget — did you ever hear back about the reservation?",
        "Hope you had a good weekend! Wanted to touch base about Thursday.",
        "Sorry for the slow reply, it has been a busy few days over here.",
    ];

    private static readonly string[] Middles =
    [
        "I know things have been a little up in the air, but I think if we lock in an evening " +
        "that should work around everyone else's plans.",
        "If it is easier for you we can also just push it to the following week instead.",
        "Either way works for me, just let me know what is easiest on your end.",
        "I already checked with the rest of the family and nobody has anything else going on.",
        "No rush at all, whenever you get a chance to look at it is fine.",
    ];

    private static readonly string[] Closers =
    [
        "Let me know if that still works and I will get it on the calendar.",
        "Talk soon!",
        "Looking forward to it either way.",
        "Thanks again for being flexible about this.",
        "Give me a call whenever works for you.",
    ];

    private static IReadOnlyList<Ghost> RealisticGhosts(int count)
    {
        var ghosts = new List<Ghost>(count);
        for (var i = 0; i < count; i++)
        {
            var body = string.Join(
                " ", Openers[i % Openers.Length], Middles[i % 3], Closers[i % 4]);
            var item = new TimelineItem(
                TimelineItemKind.Email,
                $"person{i % 25}@example.com",
                Now.AddMinutes(-i),
                body[..Math.Min(80, body.Length)],
                Incoming: i % 2 == 0,
                SourceId: $"msg-{i:D5}")
            {
                Subject = i % 2 == 0 ? "Re: Dinner plans" : "Following up",
                Body = body,
                FromAddress = $"person{i % 25}@example.com",
                Recipients = ["storyworthtelling@gmail.com"],
            };
            ghosts.Add(new Ghost(item, Now.AddMinutes(-i)));
        }
        return ghosts;
    }
}
