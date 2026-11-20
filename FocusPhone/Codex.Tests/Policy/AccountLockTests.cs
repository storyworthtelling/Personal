using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Policy;

/// <summary>
/// Accounts are added and removed only in Admin mode (audit W7), except for an app that signs in
/// by registering an account of its own (Cigna). With one installed, the device-wide restriction
/// gives way to a lock on every other account type, so the rule still holds for everything else.
/// </summary>
public class AccountLockTests
{
    private static readonly AccountAuthenticator Google = new("com.google", "com.google.android.gms");
    private static readonly AccountAuthenticator Work = new("com.example.work", "com.example.work");
    private static readonly AccountAuthenticator Cigna = new("com.cigna.mycigna.account", "com.cigna.mobile.mycigna");

    private static PolicyContext Context(bool admin, params AccountAuthenticator[] authenticators) =>
        new(
            OwnPackages: PolicyContext.FocusPhonePackages,
            BrowserPackages: new HashSet<string>(),
            OtherHomePackages: new HashSet<string>(),
            DebuggingLockedDown: false,
            NeverVisiblePackages: new HashSet<string>(),
            AdminModeActive: admin,
            Authenticators: authenticators);

    [Fact]
    public void WithoutAnExemptApp_TheDeviceWideRestrictionStands()
    {
        var plan = PolicyPlanBuilder.Build(Context(false, Google, Work));

        Assert.Contains(AndroidUserRestrictions.NoModifyAccounts, plan.UserRestrictions);
        Assert.Empty(plan.LockedAccountTypes);
    }

    [Fact]
    public void WithCignaInstalled_EveryOtherAccountTypeIsLockedInstead()
    {
        var plan = PolicyPlanBuilder.Build(Context(false, Google, Work, Cigna));

        Assert.DoesNotContain(AndroidUserRestrictions.NoModifyAccounts, plan.UserRestrictions);
        Assert.Contains(AndroidUserRestrictions.NoModifyAccounts, AndroidUserRestrictions.ToClear(plan.UserRestrictions));
        Assert.Equal(new HashSet<string> { "com.google", "com.example.work" }, plan.LockedAccountTypes);
    }

    [Fact]
    public void InAdminMode_NothingIsLocked()
    {
        var plan = PolicyPlanBuilder.Build(Context(true, Google, Work, Cigna));

        Assert.DoesNotContain(AndroidUserRestrictions.NoModifyAccounts, plan.UserRestrictions);
        Assert.Empty(plan.LockedAccountTypes);
    }

    [Fact]
    public void ATypeAlsoRegisteredByAnotherApp_StaysLocked()
    {
        var lookalike = new AccountAuthenticator("com.cigna.mycigna.account", "com.example.other");

        var plan = PolicyPlanBuilder.Build(Context(false, Google, Cigna, lookalike));

        Assert.Contains(AndroidUserRestrictions.NoModifyAccounts, plan.UserRestrictions);
    }

    [Theory]
    [InlineData("com.cigna.mobile.mycigna", true)]
    [InlineData("com.cigna.envoy", true)]
    [InlineData("com.cignaevil.app", false)]
    [InlineData("com.example.cigna", false)]
    [InlineData("com.google.android.gms", false)]
    public void OnlyCignasNamespaceIsExempt(string package, bool exempt) =>
        Assert.Equal(exempt, AccountLock.IsExempt(package));
}
