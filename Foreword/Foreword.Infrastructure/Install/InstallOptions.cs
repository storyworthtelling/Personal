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
/// <para>So these are built fresh by the head from the phone's own settings, never deserialised
/// from a shared document, and backed up to the <c>PersonalVault</c> — the per-person file in that
/// person's own Drive, which exists for exactly this distinction.</para>
/// </summary>
public class InstallOptions
{
    /// <summary>
    /// Who this install belongs to, as an explicit choice. Null means the app works it out from the
    /// signed-in account — somebody on a shared household account has to be able to say who they
    /// are and be believed, and everybody else should never have to answer at all.
    /// </summary>
    public PersonColumn? Owner { get; set; }

    /// <summary>The account signed in here, which is what proposes the owner.</summary>
    public string AccountEmail { get; set; } = string.Empty;
}
