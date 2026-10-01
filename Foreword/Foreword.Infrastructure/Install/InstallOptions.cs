using Foreword.Domain.Models;

namespace Foreword.Infrastructure.Install;

/// <summary>
/// What is true of <b>this phone</b> rather than of the household.
///
/// <para>It is its own bag for one reason, and the reason is the vault. The household's shared
/// vault is a single encrypted file that both installs read and only one writes
/// (<c>VaultOwnership</c>), and the calendar mapping rides inside it — correctly, because which
/// calendar belongs to which column is the same fact on both phones. Whose phone this is, is not.
/// Putting it in that document would mean the second install restoring the first install's answer
/// and then quietly filing its owner's handwriting under somebody else.</para>
///
/// <para>So these are built fresh by the head from the phone's own settings and never deserialised
/// from a shared document. Nothing here is chosen, so nothing needs backing up: both are facts the
/// phone reads back from Google on a fresh install.</para>
/// </summary>
public class InstallOptions
{
    /// <summary>The account signed in here, which is what proposes the owner.</summary>
    public string AccountEmail { get; set; } = string.Empty;

    /// <summary>
    /// The account that owns the household's shared vault, as last read from it. The phone signed in
    /// to it is the account holder's, and a phone signed in to any other is the partner's — so whose
    /// phone this is never has to be asked. Cached per install because the vault is read at startup
    /// and the answer is needed on every delivery.
    /// </summary>
    public string HouseholdAccount { get; set; } = string.Empty;
}
