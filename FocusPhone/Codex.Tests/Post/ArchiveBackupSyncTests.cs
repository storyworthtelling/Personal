using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Post;
using Codex.Infrastructure.Storage;
using Codex.Tests.Support;
using Microsoft.Data.Sqlite;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Codex.Tests.Post;

// Error-handling §2 and the privacy report's §5 (high): the archive backup held every filed message in full, in
// plaintext, and was overwritten by whatever the phone held. It is now sealed under the vault passphrase and
// merged both ways on every sync, and the plaintext file is trashed only once the sealed copy is confirmed.
public class ArchiveBackupSyncTests : IDisposable
{
    private const string Pass = "invented sample passphrase";
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection connection;
    private readonly FilingStore filing;
    private readonly InMemoryDriveTextFile sealedDrive = new();
    private readonly InMemoryDriveTextFile legacyDrive = new();

    public ArchiveBackupSyncTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        filing = new FilingStore(connection);
    }

    public void Dispose() => connection.Dispose();

    private ArchiveBackupSync Sync(string? passphrase = Pass) => new(sealedDrive, legacyDrive, () => passphrase);

    private static Ghost Filed(string id) =>
        new(new TimelineItem(TimelineItemKind.Email, "sam@example.com", Now.AddDays(-1), "invented " + id, true, id), Now.AddHours(-1));

    private IReadOnlyList<string> LocalIds() => filing.LoadArchive().All.Select(g => g.Item.SourceId).Order(StringComparer.Ordinal).ToList();

    private ArchiveSnapshot SealedSnapshot() =>
        ArchiveSnapshotCodec.Decode(VaultEnvelope.Read(sealedDrive.Content, Pass).Plaintext)!;

    [Fact]
    public async Task ThePlaintextBackupIsMovedIntoTheSealedFileThenTrashed()
    {
        legacyDrive.Content = ArchiveSnapshotCodec.Encode(new ArchiveSnapshot([Filed("m1"), Filed("m2")], Now));

        var result = await Sync().SyncAsync(filing, Now, default);

        Assert.Equal(ArchiveSyncOutcome.Synced, result.Outcome);
        Assert.Equal(["m1", "m2"], LocalIds());
        Assert.Equal(2, SealedSnapshot().Ghosts.Count);
        Assert.DoesNotContain("invented", sealedDrive.Content!, StringComparison.Ordinal);
        Assert.True(legacyDrive.Trashed);
    }

    [Fact]
    public async Task AFirstFilingOnAPhoneWhoseRestoreNeverRanIsAddedNotSwapped()
    {
        sealedDrive.Content = VaultEnvelope.Write(ArchiveSnapshotCodec.Encode(new ArchiveSnapshot([Filed("m1"), Filed("m2")], Now)), Pass);
        filing.SaveGhost(Filed("m9"));

        await Sync().SyncAsync(filing, Now, default);

        Assert.Equal(["m1", "m2", "m9"], LocalIds());
        Assert.Equal(3, SealedSnapshot().Ghosts.Count);
    }

    [Fact]
    public async Task AnUnfiledMessageStaysOutOfBothCopies()
    {
        filing.SaveGhost(Filed("m1"));
        filing.SaveGhost(Filed("m2"));
        await Sync().SyncAsync(filing, Now, default);

        filing.RemoveGhost("m1", Now);
        await Sync().SyncAsync(filing, Now, default);

        Assert.Equal(["m2"], LocalIds());
        Assert.Equal(["m2"], SealedSnapshot().Ghosts.Select(g => g.Item.SourceId));
    }

    [Fact]
    public async Task WithNoPassphraseThePlaintextBackupIsStillRestoredButNothingIsWrittenOrTrashed()
    {
        legacyDrive.Content = ArchiveSnapshotCodec.Encode(new ArchiveSnapshot([Filed("m1")], Now));

        var result = await Sync(passphrase: null).SyncAsync(filing, Now, default);

        Assert.Equal(ArchiveSyncOutcome.NoPassphrase, result.Outcome);
        Assert.Equal(["m1"], LocalIds());
        Assert.Null(sealedDrive.Content);
        Assert.False(legacyDrive.Trashed);
    }

    [Fact]
    public async Task ASealedFileThatWillNotOpenIsLeftAloneAndThePlaintextKept()
    {
        sealedDrive.Content = VaultEnvelope.Write(ArchiveSnapshotCodec.Encode(new ArchiveSnapshot([Filed("m1")], Now)), "another passphrase");
        var before = sealedDrive.Content;
        legacyDrive.Content = ArchiveSnapshotCodec.Encode(new ArchiveSnapshot([Filed("m2")], Now));
        filing.SaveGhost(Filed("m3"));

        var result = await Sync().SyncAsync(filing, Now, default);

        Assert.Equal(ArchiveSyncOutcome.Refused, result.Outcome);
        Assert.Equal(before, sealedDrive.Content);
        Assert.False(legacyDrive.Trashed);
        Assert.Equal(["m2", "m3"], LocalIds());
    }

    [Fact]
    public async Task DriveUnreachableChangesNothing()
    {
        sealedDrive.FailReads = true;
        legacyDrive.FailReads = true;
        filing.SaveGhost(Filed("m1"));

        var result = await Sync().SyncAsync(filing, Now, default);

        Assert.Equal(ArchiveSyncOutcome.Unreachable, result.Outcome);
        Assert.Equal(["m1"], LocalIds());
    }

    [Fact]
    public async Task AnUnchangedArchiveIsNotWrittenAgain()
    {
        filing.SaveGhost(Filed("m1"));
        await Sync().SyncAsync(filing, Now, default);
        var first = sealedDrive.Content;

        var result = await Sync().SyncAsync(filing, Now.AddMinutes(30), default);

        Assert.Equal(ArchiveSyncOutcome.Unchanged, result.Outcome);
        Assert.Equal(first, sealedDrive.Content);
    }

    // Efficiency audit §1: every sync downloaded and deserialized the whole backup only to find it unchanged. A
    // marker of what was last confirmed (the phone's archive and Drive's revision) now skips both.
    private readonly MemoryMarker marker = new();

    private ArchiveBackupSync Marked(string? passphrase = Pass) => new(sealedDrive, legacyDrive, () => passphrase, marker);

    [Fact]
    public async Task AnUnchangedArchiveIsNotDownloadedAgain()
    {
        filing.SaveGhost(Filed("m1"));
        await Marked().SyncAsync(filing, Now, default);
        var sealedReads = sealedDrive.Reads;
        var legacyReads = legacyDrive.Reads;

        var result = await Marked().SyncAsync(filing, Now.AddMinutes(30), default);

        Assert.Equal(ArchiveSyncOutcome.Unchanged, result.Outcome);
        Assert.Equal(sealedReads, sealedDrive.Reads);
        Assert.Equal(legacyReads, legacyDrive.Reads);
    }

    [Fact]
    public async Task ANewFilingIsBackedUpDespiteTheMarker()
    {
        filing.SaveGhost(Filed("m1"));
        await Marked().SyncAsync(filing, Now, default);

        filing.SaveGhost(Filed("m2"));
        var result = await Marked().SyncAsync(filing, Now.AddMinutes(30), default);

        Assert.Equal(ArchiveSyncOutcome.Synced, result.Outcome);
        Assert.Equal(2, SealedSnapshot().Ghosts.Count);
    }

    [Fact]
    public async Task AnUnfilingIsBackedUpDespiteTheMarker()
    {
        filing.SaveGhost(Filed("m1"));
        filing.SaveGhost(Filed("m2"));
        await Marked().SyncAsync(filing, Now, default);

        filing.RemoveGhost("m1", Now);
        var result = await Marked().SyncAsync(filing, Now.AddMinutes(30), default);

        Assert.Equal(ArchiveSyncOutcome.Synced, result.Outcome);
        Assert.Equal(["m2"], SealedSnapshot().Ghosts.Select(g => g.Item.SourceId));
    }

    [Fact]
    public async Task ABackupWrittenElsewhereIsReadBecauseItsRevisionMoved()
    {
        filing.SaveGhost(Filed("m1"));
        await Marked().SyncAsync(filing, Now, default);

        await sealedDrive.WriteAsync(VaultEnvelope.Write(ArchiveSnapshotCodec.Encode(new ArchiveSnapshot([Filed("m1"), Filed("m7")], Now)), Pass), default);
        var result = await Marked().SyncAsync(filing, Now.AddMinutes(30), default);

        Assert.Equal(1, result.Restored);
        Assert.Equal(["m1", "m7"], LocalIds());
    }

    [Fact]
    public async Task NothingIsSkippedWhileThePlaintextBackupIsStillThere()
    {
        legacyDrive.Content = ArchiveSnapshotCodec.Encode(new ArchiveSnapshot([Filed("m1")], Now));
        await Marked(passphrase: null).SyncAsync(filing, Now, default);
        var legacyReads = legacyDrive.Reads;

        await Marked(passphrase: null).SyncAsync(filing, Now.AddMinutes(30), default);

        Assert.Equal(legacyReads + 1, legacyDrive.Reads);
    }

    [Fact]
    public void TheArchiveIsMeasuredWithoutReadingAnyGhost()
    {
        filing.SaveGhost(Filed("m1"));
        filing.SaveGhost(Filed("m2"));

        var size = filing.MeasureArchive();

        Assert.Equal(2, size.Count);
        Assert.True(size.Bytes > 0);
    }

    private sealed class MemoryMarker : IArchiveBackupMarker
    {
        private string? value;

        public string? Read() => value;

        public void Write(string marker) => value = marker;
    }
}
