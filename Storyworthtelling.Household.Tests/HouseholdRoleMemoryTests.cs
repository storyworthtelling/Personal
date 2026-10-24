using Storyworthtelling.Household;

namespace Storyworthtelling.Household.Tests;

/// <summary>
/// After a cold start the vault has not been read, so the live role is Unknown. A phone that was shared
/// the household must not become an owner's phone for that window, so the last role the vault said is
/// remembered on the install (never in the shared file) and used until the vault is read again.
/// The policy for a role nobody has ever known is stated here too.
/// </summary>
public class HouseholdRoleMemoryTests
{
    private const string Account = "partner@example.com";

    [Fact]
    public void ALiveRoleOutranksAnyMemory() =>
        Assert.Equal(HouseholdFileRole.Owner, HouseholdRoleMemory.Effective(
            HouseholdFileRole.Owner, new RememberedRole(HouseholdFileRole.SharedWith, Account), Account));

    [Fact]
    public void AnUnreadVaultFallsBackToTheRememberedRoleForTheSameAccount() =>
        Assert.Equal(HouseholdFileRole.SharedWith, HouseholdRoleMemory.Effective(
            HouseholdFileRole.Unknown, new RememberedRole(HouseholdFileRole.SharedWith, Account), "Partner@Example.com"));

    [Fact]
    public void AMemoryOfAnotherAccountIsNotTrusted() =>
        Assert.Equal(HouseholdFileRole.Unknown, HouseholdRoleMemory.Effective(
            HouseholdFileRole.Unknown, new RememberedRole(HouseholdFileRole.SharedWith, Account), "someone@example.com"));

    [Fact]
    public void NoMemoryAndNoLiveRoleIsUnknown()
    {
        Assert.Equal(HouseholdFileRole.Unknown, HouseholdRoleMemory.Effective(HouseholdFileRole.Unknown, null, Account));
        Assert.Equal(HouseholdFileRole.Unknown, HouseholdRoleMemory.Effective(HouseholdFileRole.Unknown, new RememberedRole(HouseholdFileRole.Owner, Account), null));
    }

    /// <summary>The Unknown policy, the half the library owns: a role nobody ever knew never creates the shared file in Drive. What the role may edit is the app's.</summary>
    [Fact]
    public void ARoleNobodyKnewNeverCreatesTheFile() =>
        Assert.False(HouseholdSync.MayCreateFile(HouseholdFileRole.Unknown));

    [Fact]
    public void ARememberedSharedWithPhoneStaysSharedWithAndCreatesNothing()
    {
        var role = HouseholdRoleMemory.Effective(HouseholdFileRole.Unknown, new RememberedRole(HouseholdFileRole.SharedWith, Account), Account);

        Assert.Equal(HouseholdFileRole.SharedWith, role);
        Assert.False(HouseholdSync.MayCreateFile(role));
    }

    [Fact]
    public void AnUnknownRoleIsNeverRemembered() =>
        Assert.Throws<ArgumentException>(() => new RememberedRole(HouseholdFileRole.Unknown, Account));
}
