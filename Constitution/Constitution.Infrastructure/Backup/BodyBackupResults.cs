namespace Constitution.Infrastructure.Backup;

/// <summary>How restoring the Body backup onto this install went.</summary>
public enum BodyRestoreOutcome
{
    /// <summary>The backup was read and its history put into this install's empty store.</summary>
    Restored,

    /// <summary>This install already holds Body history, so the backup was left alone and not even opened.</summary>
    SkippedNotEmpty,

    /// <summary>There is no backup in Drive yet, or it holds no readings: nothing to restore.</summary>
    NoBackup,

    /// <summary>A backup exists and the passphrase does not open it.</summary>
    WrongPassphrase,

    /// <summary>A backup exists and is not a Body backup; no passphrase fixes that.</summary>
    Corrupt,

    /// <summary>Drive could not be reached (no network, or no Drive access granted).</summary>
    Unreachable,

    /// <summary>No passphrase was given, so Drive was not touched.</summary>
    NoPassphrase,
}

/// <summary>How backing this install's Body history up went.</summary>
public enum BodyBackupOutcome
{
    /// <summary>The history was merged with what was in Drive and the sealed file written and checked.</summary>
    BackedUp,

    /// <summary>Drive already holds everything this install has, so nothing was written.</summary>
    Unchanged,

    /// <summary>This install has no Body history, so there is nothing to back up and the file in Drive was not touched.</summary>
    NothingToBackup,

    /// <summary>No passphrase was given, so Drive was not touched.</summary>
    NoPassphrase,

    /// <summary>A file exists that could not be read (wrong passphrase, or not a Body backup), so nothing was written over it.</summary>
    RefusedUnreadable,

    /// <summary>The file holds something this version does not understand (a newer format, a signal it does not know), so nothing was written over it.</summary>
    RefusedNewerFormat,

    /// <summary>Drive could not be reached, or the check of what was written could not be read; nothing is lost and the next attempt finds out.</summary>
    Unreachable,

    /// <summary>Drive refused the write, or what landed was not readable and the previous file was put back.</summary>
    Failed,
}

/// <summary>
/// What a restore did. Carries an outcome and, when something went wrong, a reason: the failure's type and
/// message with the passphrase taken out, or what is wrong with the shape of a file that is not a backup.
/// It cannot hold a reading, a signal or a date.
/// </summary>
/// <param name="Outcome">How it went.</param>
/// <param name="Detail">When Drive was unreachable or the file was damaged: why, passphrase removed; never what the file held.</param>
public sealed record BodyRestoreResult(BodyRestoreOutcome Outcome, string? Detail = null)
{
    /// <summary>
    /// Whether the owner has something to act on: a backup that exists and will not open. An absent
    /// backup is a normal first run, and an unreachable Drive is ordinary offline; neither nags.
    /// </summary>
    public bool NeedsAttention => Outcome is BodyRestoreOutcome.WrongPassphrase or BodyRestoreOutcome.Corrupt;
}

/// <summary>
/// What a backup did. Carries an outcome and, when something went wrong, a reason: the failure's type and
/// message with the passphrase taken out, or what is wrong with the shape of a file that is not a backup.
/// It cannot hold a reading, a signal or a date.
/// </summary>
/// <param name="Outcome">How it went.</param>
/// <param name="Detail">When Drive failed or the file was damaged: why, passphrase removed; never what the file held.</param>
public sealed record BodyBackupResult(BodyBackupOutcome Outcome, string? Detail = null)
{
    /// <summary>
    /// Whether the owner has something to act on: a backup that was meant to happen and did not. An
    /// unreachable Drive is ordinary offline and tries again by itself; a newer-format file is an older
    /// build waiting for its update.
    /// </summary>
    public bool NeedsAttention => Outcome is BodyBackupOutcome.RefusedUnreadable or BodyBackupOutcome.Failed;
}
