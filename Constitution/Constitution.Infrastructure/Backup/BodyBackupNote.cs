using System.Text.RegularExpressions;

namespace Constitution.Infrastructure.Backup;

/// <summary>
/// The one inline sentence Settings can show about the Body backup: what happened to it, in plain
/// words. Wording lives here, pure, so every way a restore, a backup or a run can end has its sentence
/// and the screen only paints it. No toast, and no success the backup never saw.
///
/// <para>A sentence carries no reading, no number, no passphrase and no failure message. A failure is
/// named by its type only (when the detail starts with one): the message comes from Drive and the HTTP
/// stack, or describes the shape of a file, and is not ours to vouch for as free of a value.</para>
///
/// <para>An outcome this wording does not know is refused rather than voiced as something else, so a
/// new outcome cannot reach the screen worded as another.</para>
/// </summary>
public static class BodyBackupNote
{
    private static readonly Regex TypeShape = new("^[A-Z][A-Za-z0-9]*(Exception|Error)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The sentence for a restore.</summary>
    /// <exception cref="ArgumentNullException">There is no result.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The outcome is not one this wording knows.</exception>
    public static string For(BodyRestoreResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        switch (result.Outcome)
        {
            case BodyRestoreOutcome.Restored:
                return "Body history was brought back from the backup.";
            case BodyRestoreOutcome.SkippedNotEmpty:
                return "This phone already has Body history, so the backup was left as it is.";
            case BodyRestoreOutcome.NoBackup:
                return "There is no Body backup yet. One is made once Body has read.";
            case BodyRestoreOutcome.WrongPassphrase:
                return "The vault passphrase does not open the Body backup. It was left as it is; check the passphrase.";
            case BodyRestoreOutcome.Corrupt:
                return "The Body backup file cannot be read, so it was left as it is.";
            case BodyRestoreOutcome.Unreachable:
                return $"Drive could not be reached, so the Body backup was not checked{Named(result.Detail)}. It is tried again next time.";
            case BodyRestoreOutcome.NoPassphrase:
                return "Add the vault passphrase to bring Body history back from the backup.";
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Not a restore outcome this wording knows.");
        }
    }

    /// <summary>The sentence for a backup.</summary>
    /// <exception cref="ArgumentNullException">There is no result.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The outcome is not one this wording knows.</exception>
    public static string For(BodyBackupResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        switch (result.Outcome)
        {
            case BodyBackupOutcome.BackedUp:
                return "Body history is backed up.";
            case BodyBackupOutcome.Unchanged:
                return "The Body backup is already up to date.";
            case BodyBackupOutcome.NothingToBackup:
                return "There is no Body history to back up yet.";
            case BodyBackupOutcome.NoPassphrase:
                return "Add the vault passphrase to back up Body history.";
            case BodyBackupOutcome.RefusedUnreadable:
                return "The Body backup was left alone: the passphrase does not open it, or the file is damaged.";
            case BodyBackupOutcome.RefusedNewerFormat:
                return "The Body backup comes from a newer version of Constitution, so it was left alone. Update the app to keep backing up.";
            case BodyBackupOutcome.Unreachable:
                return $"Drive could not be reached, so Body was not backed up{Named(result.Detail)}. It is tried again by itself.";
            case BodyBackupOutcome.Failed:
                return $"Drive did not take the Body backup{Named(result.Detail)}. It is tried again by itself.";
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Not a backup outcome this wording knows.");
        }
    }

    /// <summary>The sentence for a run: a restore or a backup speaks for itself, and the rest are said here.</summary>
    /// <exception cref="ArgumentNullException">There is no result.</exception>
    /// <exception cref="ArgumentException">The run says it restored or backed up and carries no result of it.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The status is not one this wording knows.</exception>
    public static string For(BodyBackupRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        switch (result.Status)
        {
            case BodyBackupRunStatus.NoPassphrase:
                return "Add the vault passphrase to back up Body history.";
            case BodyBackupRunStatus.NotSignedIn:
                return "Sign in with Google to back up Body history.";
            case BodyBackupRunStatus.RestoreRan:
                return result.Restore is { } restore
                    ? For(restore)
                    : throw new ArgumentException("A run that restored must carry how the restore went.", nameof(result));
            case BodyBackupRunStatus.BackupRan:
                return result.Backup is { } backup
                    ? For(backup)
                    : throw new ArgumentException("A run that backed up must carry how the backup went.", nameof(result));
            case BodyBackupRunStatus.NotDue:
                return "No Body backup is due yet.";
            case BodyBackupRunStatus.SyncDidNotRun:
                return "Body has not read yet, so there is nothing new to back up.";
            case BodyBackupRunStatus.AlreadyRestored:
                return "The Body backup was already checked when the app opened.";
            case BodyBackupRunStatus.Failed:
                return $"The Body backup could not run{Named(result.Detail)}.";
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Not a run status this wording knows.");
        }
    }

    // A detail reads "TypeName: message", or is a bare type name, or is a sentence from the backup about
    // what it did. Only something shaped like a type name is repeated; anything else is dropped, because
    // a message is exactly the part that could carry a value.
    private static string Named(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return string.Empty;
        }

        var colon = detail.IndexOf(':', StringComparison.Ordinal);
        var head = (colon < 0 ? detail : detail[..colon]).Trim();
        return TypeShape.IsMatch(head) ? $" ({head})" : string.Empty;
    }
}
