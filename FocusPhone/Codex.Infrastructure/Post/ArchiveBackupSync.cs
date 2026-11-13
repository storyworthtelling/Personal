using Codex.Domain.Post;
using Codex.Infrastructure.Storage;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Codex.Infrastructure.Post;

public enum ArchiveSyncOutcome
{
    /// <summary>Merged both ways, and the backup written where it had changed.</summary>
    Synced,

    /// <summary>Merged; the backup already held all of it.</summary>
    Unchanged,

    /// <summary>No vault passphrase: the old plaintext backup was read back, but nothing was sealed or written.</summary>
    NoPassphrase,

    /// <summary>The sealed backup would not open (another passphrase, damaged, newer); it was left exactly as it is.</summary>
    Refused,

    /// <summary>Drive could not be read; nothing changed.</summary>
    Unreachable,

    /// <summary>The sealed write did not land or did not read back; the old plaintext file is kept.</summary>
    NotConfirmed,
}

/// <summary>Where the backup sync keeps what it last confirmed (Post keeps it in its preferences).</summary>
public interface IArchiveBackupMarker
{
    string? Read();

    void Write(string marker);
}

public sealed record ArchiveSyncResult(ArchiveSyncOutcome Outcome, int Restored, string? Detail = null);

/// <summary>
/// Keeps the ghost archive and its Drive backup in step on every sync (error-handling audit §2, privacy report §5).
///
/// <para>Both directions, every time: what the backup holds and the phone does not is restored, and what the phone
/// holds is added to the backup (<see cref="ArchiveSync"/>), so a restore that never ran is simply done on the next
/// sync, and the first message filed afterwards is added to the backup rather than replacing it.</para>
///
/// <para>The backup is sealed under the vault passphrase through <see cref="SealedDriveFile"/>, because it holds the
/// full text of every filed message. It lives under a new name, so an older Post that still writes plaintext can
/// never write over it. The old plaintext file is read on every sync until it is gone, and trashed only after the
/// sealed file is confirmed to hold everything it held.</para>
/// </summary>
public sealed class ArchiveBackupSync
{
    /// <summary>The sealed backup.</summary>
    public const string FileName = "storyworthtelling-post-archive-sealed.json";

    /// <summary>The plaintext backup older builds wrote; read until it has been moved into the sealed one.</summary>
    public const string LegacyFileName = "storyworthtelling-post-archive.json";

    private readonly SealedDriveFile sealedFile;
    private readonly IDriveTextFile legacyFile;
    private readonly Func<string?> passphrase;
    private readonly IDriveFileRevision? revision;
    private readonly IArchiveBackupMarker? marker;

    /// <param name="marker">What the last confirmed sync saw: the phone's archive (<see cref="FilingStore.Fingerprint"/>)
    /// and the backup's Drive revision. When both still match, the sync stops after one small revision request
    /// instead of downloading and decrypting the whole backup (efficiency audit §1). Null syncs in full every time.</param>
    public ArchiveBackupSync(IDriveTextFile sealedFile, IDriveTextFile legacyFile, Func<string?> passphrase, IArchiveBackupMarker? marker = null)
    {
        this.sealedFile = new SealedDriveFile(sealedFile);
        revision = sealedFile as IDriveFileRevision;
        this.marker = marker;
        this.legacyFile = legacyFile;
        this.passphrase = passphrase;
    }

    public async Task<ArchiveSyncResult> SyncAsync(FilingStore filing, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (marker?.Read() is { } confirmed && revision is not null && !string.IsNullOrEmpty(passphrase()))
        {
            try
            {
                if (confirmed == Mark(filing.Fingerprint(), await revision.RevisionAsync(cancellationToken)))
                {
                    return new ArchiveSyncResult(ArchiveSyncOutcome.Unchanged, 0);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new ArchiveSyncResult(ArchiveSyncOutcome.Unreachable, 0, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        var result = await SyncInFullAsync(filing, now, cancellationToken);
        await RememberAsync(filing, result, cancellationToken);
        return result;
    }

    // Kept only after a sync that left both copies equal with the plaintext file gone, so a skipped sync never
    // skips work that was owed.
    private async Task RememberAsync(FilingStore filing, ArchiveSyncResult result, CancellationToken cancellationToken)
    {
        if (marker is null || revision is null || result.Outcome is not (ArchiveSyncOutcome.Synced or ArchiveSyncOutcome.Unchanged))
        {
            return;
        }

        try
        {
            marker.Write(Mark(filing.Fingerprint(), await revision.RevisionAsync(cancellationToken)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No marker: the next sync is a full one, which is only what every sync used to be.
        }
    }

    private static string Mark(string fingerprint, string? driveRevision) => fingerprint + "|" + driveRevision;

    private async Task<ArchiveSyncResult> SyncInFullAsync(FilingStore filing, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var local = new ArchiveSnapshot(filing.LoadArchive().All, now) { Unfiled = filing.LoadUnfiled() };

        ArchiveSnapshot? legacy;
        try
        {
            legacy = ArchiveSnapshotCodec.Decode(await legacyFile.ReadAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ArchiveSyncResult(ArchiveSyncOutcome.Unreachable, 0, $"{ex.GetType().Name}: {ex.Message}");
        }

        var key = passphrase();
        if (string.IsNullOrEmpty(key))
        {
            // Nothing can be sealed, so nothing is written; the plaintext copy can still be taken back down.
            return new ArchiveSyncResult(ArchiveSyncOutcome.NoPassphrase, Adopt(filing, local, ArchiveSync.Merge(local, legacy)));
        }

        var read = await sealedFile.ReadAsync(key, cancellationToken);
        if (read.Status == SealedReadStatus.Unreachable)
        {
            return new ArchiveSyncResult(ArchiveSyncOutcome.Unreachable, 0, read.Detail);
        }
        if (!read.Writable)
        {
            return new ArchiveSyncResult(ArchiveSyncOutcome.Refused, Adopt(filing, local, ArchiveSync.Merge(local, legacy)),
                $"The sealed archive backup did not open ({read.Status}); it is left exactly as it is.");
        }

        var backedUp = read.Status == SealedReadStatus.Loaded ? ArchiveSnapshotCodec.Decode(read.Plaintext) : null;
        if (read.Status == SealedReadStatus.Loaded && backedUp is null)
        {
            return new ArchiveSyncResult(ArchiveSyncOutcome.Refused, 0, "The sealed archive backup opened but is not an archive; it is left as it is.");
        }

        var merged = ArchiveSync.Merge(ArchiveSync.Merge(local, backedUp), legacy);
        var restored = Adopt(filing, local, merged);

        if (!ArchiveSync.NeedsWriting(merged, backedUp))
        {
            await TrashLegacyAsync(legacy, cancellationToken);
            return new ArchiveSyncResult(ArchiveSyncOutcome.Unchanged, restored);
        }

        var written = await sealedFile.WriteAsync(ArchiveSnapshotCodec.Encode(merged), key, read, SealedWriteOptions.Default, cancellationToken);
        if (!written.Succeeded)
        {
            return new ArchiveSyncResult(ArchiveSyncOutcome.NotConfirmed, restored, written.Detail);
        }

        await TrashLegacyAsync(legacy, cancellationToken);
        return new ArchiveSyncResult(ArchiveSyncOutcome.Synced, restored);
    }

    // The merge becomes the phone's archive: missing ghosts restored, ghosts unfiled elsewhere taken out, and
    // every unfiled entry kept so a later merge honours it after a restart.
    private static int Adopt(FilingStore filing, ArchiveSnapshot local, ArchiveSnapshot merged)
    {
        var restore = ArchiveSync.ToRestore(merged, local);
        foreach (var ghost in restore)
        {
            filing.SaveGhost(ghost);
        }

        var kept = merged.Ghosts.Select(ghost => ghost.Item.SourceId).ToHashSet(StringComparer.Ordinal);
        var unfiled = merged.Unfiled.ToDictionary(entry => entry.SourceId, StringComparer.Ordinal);
        foreach (var ghost in local.Ghosts.Where(ghost => !kept.Contains(ghost.Item.SourceId)))
        {
            filing.RemoveGhost(ghost.Item.SourceId, unfiled.TryGetValue(ghost.Item.SourceId, out var entry) ? entry.UnfiledAtUtc : DateTimeOffset.UtcNow);
        }

        filing.RememberUnfiled(merged.Unfiled);
        return restore.Count;
    }

    // Only once the sealed file holds everything the plaintext one did (the caller has just confirmed that).
    private async Task TrashLegacyAsync(ArchiveSnapshot? legacy, CancellationToken cancellationToken)
    {
        if (legacy is not null && legacyFile is IDriveFileTrash trash)
        {
            await trash.TrashAsync(cancellationToken);
        }
    }
}
