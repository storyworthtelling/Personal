namespace Storyworthtelling.Voice.Secrets;

// Where the encrypted vault lives. The production store keeps it as one JSON file in the owner's Google
// Drive, found by name, alongside the voice profile — so every app reads and writes the same vault.
public interface ISecretsVaultStore
{
    // Reads and decrypts the vault. The result distinguishes "no vault yet" (first run — nothing to
    // restore), a wrong passphrase, and a corrupt file from a successful load, so a restore flow can react
    // appropriately instead of silently treating every failure as empty.
    Task<SecretsVaultLoadResult> LoadAsync(string passphrase, CancellationToken cancellationToken);

    // Encrypts and writes the whole vault over the file as it is now, under the sealed-file rule (G14).
    Task SaveAsync(SecretsVault vault, string passphrase, CancellationToken cancellationToken);

    // Writes over the file exactly as `basis` read it: a file another phone wrote since then is a
    // VaultWriteException with a Conflict outcome, to read again and merge. Stores with no revisions save plainly.
    Task SaveAsync(SecretsVault vault, string passphrase, SecretsVaultLoadResult basis, CancellationToken cancellationToken) =>
        SaveAsync(vault, passphrase, cancellationToken);
}

/// <summary>A sealed write that did not land: refused, conflicted, not confirmed or failed. Never carries the vault.</summary>
public sealed class VaultWriteException(SealedWriteResult result)
    : IOException(result.Detail ?? $"The vault was not written ({result.Outcome}).")
{
    public SealedWriteResult Result { get; } = result;

    public bool IsConflict => Result.Outcome == SealedWriteOutcome.Conflict;
}

public enum SecretsVaultStatus
{
    // No vault file exists yet (or it is blank) — nothing has ever been published.
    Empty,

    // The vault was read and decrypted successfully.
    Loaded,

    // A vault file exists but the passphrase is wrong (or the file was tampered with).
    WrongPassphrase,

    // A vault file exists but isn't a valid envelope — not something a passphrase can fix.
    Corrupt
}

// The outcome of a load. Vault is meaningful only when Status is Loaded; otherwise it is SecretsVault.Empty.
// FileOwnership is whether the vault file belongs to the signed-in account (the admin's) or was shared with
// it. It decides who may write: the admin's install writes, everyone else reads. The default is None (no file
// seen), which is claimable; DriveSecretsVaultStore reports Shared for any file that cannot say, so an unknown
// file is never claimed.
public sealed record SecretsVaultLoadResult(
    SecretsVaultStatus Status, SecretsVault Vault, DriveFileOwnership FileOwnership = DriveFileOwnership.None)
{
    /// <summary>The read this result came from, which a save writes over. Null from a store without one.</summary>
    public SealedRead? Basis { get; init; }

    public static SecretsVaultLoadResult Empty { get; } = new(SecretsVaultStatus.Empty, SecretsVault.Empty);
    public static SecretsVaultLoadResult WrongPassphrase { get; } = new(SecretsVaultStatus.WrongPassphrase, SecretsVault.Empty);
    public static SecretsVaultLoadResult Corrupt { get; } = new(SecretsVaultStatus.Corrupt, SecretsVault.Empty);

    public static SecretsVaultLoadResult Loaded(SecretsVault vault) => new(SecretsVaultStatus.Loaded, vault);
}
