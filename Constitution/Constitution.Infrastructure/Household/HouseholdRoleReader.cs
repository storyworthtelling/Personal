using Storyworthtelling.Household;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.Infrastructure.Household;

/// <summary>What reading the vault's owner marker gave: the role, and the Drive file ids the vault remembers (so a file shared to this account is found by id).</summary>
public sealed record HouseholdRoleRead(HouseholdFileRole Role, IReadOnlyDictionary<string, string> DriveFileIds)
{
    public static HouseholdRoleRead None { get; } = new(HouseholdFileRole.Unknown, new Dictionary<string, string>());
}

/// <summary>
/// Whose phone this is, read the way every sibling app reads it: the vault's owner (<see cref="VaultOwnership"/>), so the vault and
/// the household can never disagree about whose Drive holds the file. Reads only; the vault is never written from here. A vault
/// that was not opened (empty, wrong passphrase, corrupt), no passphrase and no account all say Unknown, which has the owner's
/// rights over the roster and never creates the shared file. A store that cannot be reached throws what it threw, for the caller to
/// log and carry on with the role it remembers.
/// </summary>
public static class HouseholdRoleReader
{
    public static async Task<HouseholdRoleRead> ReadAsync(
        ISecretsVaultStore store, string? passphrase, string? account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (string.IsNullOrEmpty(passphrase) || string.IsNullOrWhiteSpace(account))
        {
            return HouseholdRoleRead.None;
        }

        var loaded = await new SecretsVaultService(store, () => passphrase).LoadAsync(cancellationToken);
        if (loaded.Status != SecretsVaultStatus.Loaded)
        {
            return HouseholdRoleRead.None;
        }

        var role = VaultOwnership.Decide(loaded.FileOwnership) switch
        {
            SharedVaultAccess.ReadWrite or SharedVaultAccess.Claimable => HouseholdFileRole.Owner,
            SharedVaultAccess.ReadOnly => HouseholdFileRole.SharedWith,
            _ => HouseholdFileRole.Unknown,
        };
        return new HouseholdRoleRead(role, loaded.Vault.DriveFileIds);
    }
}
