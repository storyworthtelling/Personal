namespace Storyworthtelling.Voice.Secrets;

/// <summary>What an install may do with the household's shared vault.</summary>
public enum SharedVaultAccess
{
    /// <summary>No vault file is visible yet, so this install may create it and become its owner.</summary>
    Claimable,

    /// <summary>The vault file is owned by this account: this is the admin's install, and it reads and writes.</summary>
    ReadWrite,

    /// <summary>The vault file was shared with this account: it reads the household's keys and never writes.</summary>
    ReadOnly,

    /// <summary>Nobody is signed in, so there is no question to answer yet.</summary>
    None,
}

/// <summary>
/// Who is the admin, and therefore who may write the shared vault.
///
/// The suite is two people on two phones sharing one encrypted file. Both need to <b>read</b> it — the
/// Claude key and the TIDAL app credentials are household things — but only the admin may write it. That
/// is not a trust judgement: the vault has no conflict resolution, and two installs merging into one file
/// would eventually lose somebody's write.
///
/// The answer is the vault file's Drive ownership. The admin created the file, so Drive lists this account
/// as its owner; the other install has it shared with it. Neither a stamp inside the file nor the account a
/// phone happens to be signed into decides it, because the first install to publish would otherwise become
/// the admin by accident.
/// </summary>
public static class VaultOwnership
{
    public static SharedVaultAccess Decide(DriveFileOwnership ownership) => ownership switch
    {
        DriveFileOwnership.Owned => SharedVaultAccess.ReadWrite,
        DriveFileOwnership.Shared => SharedVaultAccess.ReadOnly,
        _ => SharedVaultAccess.Claimable,
    };
}

public static class SharedVaultAccessExtensions
{
    /// <summary>Whether a publish should go ahead. Claimable counts: creating the file makes this install the admin.</summary>
    public static bool CanWrite(this SharedVaultAccess access) =>
        access is SharedVaultAccess.ReadWrite or SharedVaultAccess.Claimable;
}
