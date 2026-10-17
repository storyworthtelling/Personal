using System.Text.RegularExpressions;

namespace Constitution.Infrastructure.Vault;

/// <summary>Which connection a vault note is about; it decides the words around the outcome.</summary>
public enum VaultNoteSubject
{
    /// <summary>The household's Sleep Number login was saved.</summary>
    SleepNumber,

    /// <summary>The reMarkable was paired.</summary>
    Remarkable,
}

/// <summary>
/// The one inline sentence Settings leaves behind after a connection is saved: what happened to the
/// backup, in plain words. No toast, and no success the vault never saw. Wording lives here, pure, so
/// that every way a run can end has its sentence and the screen only paints it.
///
/// <para>Whatever the vault did, the value is saved on this phone first; every sentence but the landed
/// one begins by saying so, then says what is missing. A failure is named by its type only: the message
/// comes from Drive and the HTTP stack, and is not ours to vouch for as free of a value.</para>
/// </summary>
public static class VaultNote
{
    /// <summary>The sentence for a finished publish. A restore result is not a publish and is refused.</summary>
    /// <param name="result">What the runner's publish did.</param>
    /// <param name="subject">Which connection was just saved.</param>
    /// <exception cref="ArgumentException">The result is a restore, or a publish with no result in it.</exception>
    public static string For(ConstitutionVaultRunResult result, VaultNoteSubject subject)
    {
        ArgumentNullException.ThrowIfNull(result);
        var held = HeldHere(subject);

        switch (result.Status)
        {
            case VaultRunStatus.NoPassphrase:
                return $"{held}. Add the vault passphrase to keep it in the vault.";
            case VaultRunStatus.NotSignedIn:
                return $"{held}. Sign in with Google to keep it in the vault.";
            case VaultRunStatus.Failed:
                return $"{held}, but the vault could not be updated{Named(result.Detail)}.";
            case VaultRunStatus.Published:
                if (result.Publish is null)
                {
                    throw new ArgumentException("A publish result must carry how the publish went.", nameof(result));
                }

                return ForSharedOutcome(result.Publish, subject, held);
            case VaultRunStatus.Restored:
                throw new ArgumentException("A restore says nothing about whether a saved value reached the vault.", nameof(result));
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Not a run status this wording knows.");
        }
    }

    // Only the shared vault holds these connections, so only its outcome matters here.
    private static string ForSharedOutcome(ConstitutionPublishResult publish, VaultNoteSubject subject, string held)
    {
        switch (publish.Shared)
        {
            case VaultWriteOutcome.Published:
                return subject == VaultNoteSubject.Remarkable
                    ? "Paired and saved to the vault. It is checked on the next delivery."
                    : "Saved to the vault. It is checked on the next reading.";
            case VaultWriteOutcome.NothingToPublish:
                return $"{held}. There was nothing new to send to the vault.";
            case VaultWriteOutcome.NoPassphrase:
                return $"{held}. Add the vault passphrase to keep it in the vault.";
            case VaultWriteOutcome.RefusedUnreadable:
                return $"{held}, but the vault was left alone: the passphrase does not open it, or the file is damaged.";
            case VaultWriteOutcome.RefusedNotOwner:
                return $"{held}. The vault belongs to another Google account, so only that account can change it.";
            case VaultWriteOutcome.RefusedUnknownAccount:
                return $"{held}. Sign in with Google so the vault knows whose phone this is.";
            case VaultWriteOutcome.Failed:
                return $"{held}, but Drive did not take the update{Named(publish.SharedDetail)}.";
            default:
                throw new ArgumentOutOfRangeException(nameof(publish), publish.Shared, "Not a write outcome this wording knows.");
        }
    }

    private static string HeldHere(VaultNoteSubject subject) => subject switch
    {
        VaultNoteSubject.SleepNumber => "Saved on this phone",
        VaultNoteSubject.Remarkable => "Paired and saved on this phone",
        _ => throw new ArgumentOutOfRangeException(nameof(subject), subject, "Not a subject this wording knows."),
    };

    // A detail reads "TypeName: message", or is a bare message when the failure came back from a store
    // that kept no type. Only something shaped like a type name is repeated; anything else is dropped,
    // because a message is exactly the part that could carry a value.
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

    private static readonly Regex TypeShape = new("^[A-Z][A-Za-z0-9]*(Exception|Error)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
