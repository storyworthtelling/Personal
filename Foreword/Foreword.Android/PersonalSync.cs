using Foreword.Domain.Models;
using Storyworthtelling.Voice.Secrets;

namespace Foreword.Android;

/// <summary>
/// Keeps what is true of <b>this phone</b> in this person's own vault: whose install it is, and the
/// mark its documents sign off with.
///
/// <para>Its own file, not a section of <see cref="VaultSync"/>'s, and the distinction is the whole
/// point of Release I. The shared vault is one encrypted file that both installs read and only one
/// writes, so anything in it is a household fact by construction — putting the owner's column there
/// would mean the second install restoring the first install's answer and then filing its owner's
/// handwriting under somebody else's name. Nothing would error. The morning would simply belong to
/// the wrong person.</para>
///
/// <para>Follows Score, which already keeps a TIDAL sign-in this way — the same
/// <see cref="PersonalVaultAccess"/>, now shared out of the FocusPhone UI library so there is one
/// implementation of "refuses to write a vault it could not read" rather than two.</para>
///
/// <para>Best-effort in both directions, like everything else vault-backed. The phone is the source
/// of truth; this is the off-device backup.</para>
/// </summary>
public sealed class PersonalSync(PersonalVaultAccess personal)
{
    public Task<bool> PublishAsync(CancellationToken cancellationToken) =>
        personal.PublishAsync(vault => vault with
        {
            OwnerColumn = CredentialStore.OwnerColumn?.ToString() ?? string.Empty,
            OwnerPhoto = CredentialStore.OwnerPhoto,
        }, cancellationToken);

    /// <summary>
    /// Fills in what this install is missing. A local value always wins — a restore must never
    /// overwrite something the owner has already answered on this phone.
    /// </summary>
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var loaded = await personal.LoadAsync(cancellationToken);
        if (loaded.Status != PersonalVaultStatus.Loaded)
        {
            return;
        }

        if (CredentialStore.OwnerColumn is null
            && Enum.TryParse<PersonColumn>(loaded.Vault.OwnerColumn, out var column))
        {
            CredentialStore.OwnerColumn = column;
        }

        if (string.IsNullOrEmpty(CredentialStore.OwnerPhoto)
            && !string.IsNullOrEmpty(loaded.Vault.OwnerPhoto))
        {
            CredentialStore.OwnerPhoto = loaded.Vault.OwnerPhoto;
        }

        Apply();
    }

    /// <summary>
    /// Hands the printing library this install's mark. Called after a restore and after the owner
    /// picks a photo, so the next document composed carries it without a restart.
    /// </summary>
    public static void Apply()
    {
        var photo = CredentialStore.OwnerPhoto;

        Storyworthtelling.Paper.OwnerMark.Use(
            string.IsNullOrEmpty(photo) ? null : Convert.FromBase64String(photo));
    }
}
