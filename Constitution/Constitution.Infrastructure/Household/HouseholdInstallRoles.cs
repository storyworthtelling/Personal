using Constitution.Domain.Diagnostics;
using Storyworthtelling.Household;

namespace Constitution.Infrastructure.Household;

/// <summary>The household file's role for an install, in the head's own words. Unknown stays unknown: the Meals tab offers the owner's row until the vault says.</summary>
public static class HouseholdInstallRoles
{
    public static InstallRole For(HouseholdFileRole file) => file switch
    {
        HouseholdFileRole.Owner => InstallRole.Owner,
        HouseholdFileRole.SharedWith => InstallRole.Partner,
        HouseholdFileRole.Unknown => InstallRole.Unknown,
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, "This household role has no install role; add it here."),
    };
}
