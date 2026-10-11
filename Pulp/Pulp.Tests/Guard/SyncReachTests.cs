namespace Pulp.Tests.Guard;

/// <summary>
/// What nothing reached was never built: a pass that no screen could ask for, a notice nothing raised,
/// a passphrase a comment said was kept and was not, a launch that never read the vault. Each guard
/// reads the head's source and asserts it found the code it protects.
/// </summary>
public class SyncReachTests
{
    private static string Text(string path) => File.ReadAllText(Path.Combine(PulpSource.Root(), path));

    [Fact]
    public void TheShellAsksForAPassOnResumeAndRaisesTheSyncNotice()
    {
        var shell = Text("Pulp.Android/MainActivity.cs");

        Assert.Contains("SyncResumeGate", shell);
        Assert.Contains("HouseholdSyncNotices", shell);
        Assert.Contains("RequestSync", shell);
    }

    [Fact]
    public void ServicesRefreshTheVaultAtLaunchAndRememberTheRole()
    {
        var services = Text("Pulp.Android/App/PulpServices.cs");

        Assert.Contains("RefreshAtLaunchAsync", services);
        Assert.Contains("SaveRole", services);
        Assert.Contains("HouseholdRoleMemory.Effective", services);
    }

    [Fact]
    public void TheSyncNowButtonExistsAndCallsSyncNow()
    {
        var button = Text("Pulp.Android/Settings/SyncNowButton.cs");

        Assert.Contains("SyncLine", button);
        Assert.Contains("SyncNowAsync", button);
    }

    [Fact]
    public void FirstRunKeepsThePassphraseAsItIsTyped()
    {
        var firstRun = Text("Pulp.Android/FirstRun/FirstRunActivity.cs");

        Assert.Contains("passphrase.TextChanged", firstRun);
        Assert.Contains("VaultPassphraseStore.Passphrase = ", firstRun);
        Assert.Contains("kept as it is typed", firstRun);
    }

    [Fact]
    public void AddPeopleWritesTheChildrensRulesThroughTheSameEdit()
    {
        var services = Text("Pulp.Android/App/PulpServices.cs");

        Assert.Contains("HouseholdDraftWrite.Apply", services);
    }
}
