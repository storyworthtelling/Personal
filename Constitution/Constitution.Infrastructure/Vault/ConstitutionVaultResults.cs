namespace Constitution.Infrastructure.Vault;

/// <summary>How reading one vault went. The ways it can fail are told apart because they mean different things to the owner.</summary>
public enum VaultReadOutcome
{
    /// <summary>No passphrase was given, so the vault was not touched.</summary>
    NoPassphrase,

    /// <summary>The vault was read and decrypted.</summary>
    Loaded,

    /// <summary>There is no vault file yet, so there is nothing to restore.</summary>
    Empty,

    /// <summary>A vault exists and the passphrase does not open it.</summary>
    WrongPassphrase,

    /// <summary>A vault exists and is not a vault; no passphrase fixes that.</summary>
    Corrupt,

    /// <summary>Drive could not be reached (no network, or no Drive access granted).</summary>
    Unreachable,
}

/// <summary>How writing one vault went.</summary>
public enum VaultWriteOutcome
{
    /// <summary>The values were merged in and the vault written back.</summary>
    Published,

    /// <summary>This install has nothing to say to this vault yet, so nothing was read or written.</summary>
    NothingToPublish,

    /// <summary>No passphrase was given, so the vault was not touched.</summary>
    NoPassphrase,

    /// <summary>The vault exists and could not be read (wrong passphrase, or corrupt), so nothing was written over it.</summary>
    RefusedUnreadable,

    /// <summary>Another account owns the shared vault; this install reads it and never writes it. The design, not a fault.</summary>
    RefusedNotOwner,

    /// <summary>This install does not know which account it is signed in as, so it cannot show it owns the shared vault.</summary>
    RefusedUnknownAccount,

    /// <summary>Drive could not be reached, or refused the write.</summary>
    Failed,
}

/// <summary>
/// What a restore did. Names the connections it filled and how each vault read went; carries no value.
/// </summary>
/// <param name="Shared">How reading the household's shared vault went.</param>
/// <param name="Personal">How reading this person's own vault went.</param>
/// <param name="Filled">The connections that were missing here and are now filled, shared ones first.</param>
/// <param name="SharedDetail">When the shared vault was unreachable: the failure's type and message, passphrase removed.</param>
/// <param name="PersonalDetail">When the personal vault was unreachable: the failure's type and message, passphrase removed.</param>
public sealed record ConstitutionRestoreResult(
    VaultReadOutcome Shared,
    VaultReadOutcome Personal,
    IReadOnlyList<VaultField> Filled,
    string? SharedDetail = null,
    string? PersonalDetail = null)
{
    /// <summary>
    /// Whether the owner has something to act on: a vault that exists and will not open. An absent vault
    /// is a normal first run, and an unreachable one is ordinary offline; neither nags.
    /// </summary>
    public bool NeedsAttention =>
        Shared is VaultReadOutcome.WrongPassphrase or VaultReadOutcome.Corrupt
        || Personal is VaultReadOutcome.WrongPassphrase or VaultReadOutcome.Corrupt;
}

/// <summary>
/// What a publish did, vault by vault. Carries no value.
/// </summary>
/// <param name="Shared">How writing the household's shared vault went.</param>
/// <param name="Personal">How writing this person's own vault went.</param>
/// <param name="SharedDetail">When the shared write failed: the failure's type and message, passphrase removed.</param>
/// <param name="PersonalDetail">When the personal write failed: the failure's type and message, passphrase removed.</param>
public sealed record ConstitutionPublishResult(
    VaultWriteOutcome Shared,
    VaultWriteOutcome Personal,
    string? SharedDetail = null,
    string? PersonalDetail = null)
{
    /// <summary>
    /// Whether the owner has something to act on: a backup that was meant to happen and did not. Another
    /// account owning the shared vault is deliberately not here; that install working as designed must
    /// not nag.
    /// </summary>
    public bool NeedsAttention =>
        Shared is VaultWriteOutcome.RefusedUnreadable or VaultWriteOutcome.Failed
        || Personal is VaultWriteOutcome.RefusedUnreadable or VaultWriteOutcome.Failed;
}
