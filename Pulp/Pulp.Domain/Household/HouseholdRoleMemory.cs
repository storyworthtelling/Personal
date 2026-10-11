namespace Pulp.Domain.Household;

/// <summary>
/// The role the vault last said for an account, kept on this install. Never an Unknown role (that is the
/// absence of a memory) and never without the account it was read under.
/// </summary>
public sealed record RememberedRole
{
    public RememberedRole(HouseholdFileRole role, string account)
    {
        if (role == HouseholdFileRole.Unknown)
        {
            throw new ArgumentException("An unknown role is not worth remembering.", nameof(role));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        Role = role;
        Account = account;
    }

    public HouseholdFileRole Role { get; }

    public string Account { get; }
}

/// <summary>
/// Which role a phone acts on. After a cold start the vault has not been read yet, so the live role is
/// <see cref="HouseholdFileRole.Unknown"/>; acting on that would give the other adult's phone the owner's
/// rights. The role remembered from the last vault read stands in until the vault is read again — but only
/// for the same Google account, since another account's memory says nothing about this one.
///
/// <para><b>Unknown policy.</b> A role nobody has ever known (no vault read, nothing remembered) has the
/// owner's <i>permissions</i> — a phone with no vault must still be able to set its household up — but may
/// never create the shared file in Drive (<see cref="HouseholdSync.MayCreateFile"/>), which is the
/// irreversible half. The window is closed by reading the vault at launch.</para>
/// </summary>
public static class HouseholdRoleMemory
{
    public static HouseholdFileRole Effective(HouseholdFileRole live, RememberedRole? remembered, string? currentAccount)
    {
        if (live != HouseholdFileRole.Unknown)
        {
            return live;
        }

        return remembered is not null
            && !string.IsNullOrWhiteSpace(currentAccount)
            && string.Equals(remembered.Account, currentAccount, StringComparison.OrdinalIgnoreCase)
                ? remembered.Role
                : HouseholdFileRole.Unknown;
    }
}
