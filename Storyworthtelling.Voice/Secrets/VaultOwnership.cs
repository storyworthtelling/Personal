namespace Storyworthtelling.Voice.Secrets;

/// <summary>What an install may do with the household's shared vault.</summary>
public enum SharedVaultAccess
{
    /// <summary>Nobody is signed in, so there is no question to answer yet.</summary>
    None,

    /// <summary>Signed in, and the vault has no owner — this install may take it.</summary>
    Claimable,

    /// <summary>This is the owner's install: it reads and writes.</summary>
    ReadWrite,

    /// <summary>Somebody else's install: it reads the household's keys, never writes.</summary>
    ReadOnly,
}

/// <summary>
/// Who owns the shared vault, and therefore who may write it.
///
/// The suite is two people on two phones sharing one encrypted file. Both need to
/// <b>read</b> it — the Claude key and the TIDAL app credentials are household
/// things — but only one may write it. That is not a trust judgement: the vault has
/// no conflict resolution, and two installs merging into one file would eventually
/// lose somebody's write. For two people, deciding who writes is cheaper than
/// building a story about what happens when both do.
///
/// The answer comes from the bound Google account rather than a setting, because a
/// setting could only ever agree with the account or be wrong. The vault records the
/// account that first published to it, and every install afterwards compares itself
/// against that — so the owner becomes the owner by using it first, and nobody is
/// asked to configure anything.
/// </summary>
public static class VaultOwnership
{
    public static SharedVaultAccess Decide(string? signedInAccount, SecretsVault vault)
    {
        if (string.IsNullOrWhiteSpace(signedInAccount))
        {
            return SharedVaultAccess.None;
        }

        if (string.IsNullOrWhiteSpace(vault.OwnerAccount))
        {
            return SharedVaultAccess.Claimable;
        }

        return Same(signedInAccount, vault.OwnerAccount)
            ? SharedVaultAccess.ReadWrite
            : SharedVaultAccess.ReadOnly;
    }

    /// <summary>
    /// Stamps an unowned vault with whoever is publishing, and leaves an owned one
    /// exactly as it is.
    ///
    /// Folded into the ordinary publish merge rather than offered as its own step:
    /// claiming has to happen once and be impossible to forget, and re-stamping on
    /// every write is how the second install would quietly become the first.
    /// </summary>
    public static SecretsVault Claim(SecretsVault vault, string? signedInAccount)
    {
        if (string.IsNullOrWhiteSpace(signedInAccount) || !string.IsNullOrWhiteSpace(vault.OwnerAccount))
        {
            return vault;
        }

        return vault with { OwnerAccount = signedInAccount.Trim() };
    }

    // Google returns the address in whatever case it was typed, and an account that
    // stopped matching itself over capitalisation would lock the owner out of their
    // own vault.
    private static bool Same(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}

public static class SharedVaultAccessExtensions
{
    /// <summary>Whether a publish should go ahead. Claiming counts: taking an unowned
    /// vault happens by writing to it.</summary>
    public static bool CanWrite(this SharedVaultAccess access) =>
        access is SharedVaultAccess.ReadWrite or SharedVaultAccess.Claimable;
}
