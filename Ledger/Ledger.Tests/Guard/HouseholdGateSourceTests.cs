using System.Text.RegularExpressions;
using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Source-level halves of Phase 1.1's guards. The Android head cannot be loaded by this desktop-TFM
/// project, so what has to be true of its wiring is read out of the files themselves — the same
/// content-reading pattern <c>ProductionOnlyTests</c> and <c>IndexInvariantsTests</c> use.
/// </summary>
public class HouseholdGateSourceTests
{
    // ---- R-G1: the vault service is built knowing who is signed in ----

    /// <summary>
    /// <c>SecretsVaultService</c>'s third argument is the only thing that lets
    /// <c>VaultOwnership</c> run at all: left off, the service treats every install as "cannot tell"
    /// and publishes exactly as it did before ownership existed, so her phone can write her empty Plaid
    /// section over the owner's. Nothing fails when it is missing — the vault simply stops being
    /// protected — which is why it is read here rather than trusted.
    ///
    /// The behavioural half already exists and is not duplicated:
    /// <c>Storyworthtelling.Voice.Tests/Secrets/VaultOwnershipPublishTests.AnotherPersonsInstallDoesNotWrite</c>
    /// proves a non-owning account's <c>PublishAsync</c> is refused (ReadOnly, zero saves) once the
    /// account is supplied.
    /// </summary>
    [Fact]
    public void TheVaultServiceIsConstructedWithTheSignedInAccount()
    {
        var program = Source("Ledger.Android/MauiProgram.cs");

        var construction = Regex.Match(program, @"new SecretsVaultService\((?<args>[^;]*)\);", RegexOptions.Singleline);
        Assert.True(construction.Success, "MauiProgram no longer constructs a SecretsVaultService.");

        Assert.Contains("SettingsStore.GoogleAccountEmail", construction.Groups["args"].Value, StringComparison.Ordinal);
    }

    // ---- R-G5: her Snowball render survives the Extra field's change event ----

    /// <summary>
    /// <c>extraEntry.TextChanged</c> runs <c>OnExtraPaymentTypedAsync</c>, which re-renders the loans
    /// from the LOCAL database. On her phone that database is empty, so an unsuppressed assignment in
    /// the household render replaces the household figures with nothing. Every assignment to the
    /// field's text must therefore sit inside the suppression flag, in both renders.
    /// </summary>
    [Fact]
    public void EveryExtraEntryTextAssignmentSitsInsideTheSuppressionGuard()
    {
        var snowball = Source("Ledger.Android/Pages/SnowballPage.xaml.cs");

        var assignments = Regex.Matches(snowball, @"extraEntry\.Text\s*=[^=]");
        Assert.NotEmpty(assignments);

        var guarded = Regex.Matches(
            snowball,
            @"suppressExtraEntryTextChanged = true;\s*extraEntry\.Text = [^;]+;\s*suppressExtraEntryTextChanged = false;");

        Assert.True(
            assignments.Count == guarded.Count,
            $"{assignments.Count} assignments to extraEntry.Text but only {guarded.Count} are wrapped in "
            + "suppressExtraEntryTextChanged — an unwrapped one fires OnExtraPaymentTypedAsync against "
            + "the local (empty, on her phone) database and overwrites the household render.");
    }


    // ---- R-G2 (wiring): the launch does only what the role's plan allows ----

    /// <summary>
    /// The pure decision is <c>LaunchPlan.For</c> (tested in <see cref="HouseholdGateTests"/>); this
    /// reads the one place that has to obey it. Restoring, scheduling, the immediate refresh and the
    /// worksheet worker must each sit behind their own flag, or the plan is decided and ignored.
    /// </summary>
    [Fact]
    public void TheLaunchRestoresAndSchedulesOnlyWhatThePlanAllows()
    {
        var launch = Source("Ledger.Android/Platforms/Android/MainActivity.cs");

        Assert.Contains("LaunchPlan.For(role)", launch, StringComparison.Ordinal);
        Assert.Matches(@"if \(plan\.RestoreFromVault\)\s*\{[^}]*RestoreAsync", launch);
        Assert.Matches(@"if \(plan\.ScheduleRefresh\)\s*RefreshWorker\.Schedule", launch);
        Assert.Matches(@"if \(plan\.RunRefreshNow[^)]*\)\s*RefreshWorker\.RunNow", launch);
        Assert.Matches(@"if \(plan\.ScheduleWorksheet\)\s*WorksheetDeliveryWorker\.Schedule", launch);
    }

    /// <summary>The vault choke point refuses for her install too, so no caller has to remember.</summary>
    [Fact]
    public void VaultSyncRefusesToRestoreOrPublishOnHerInstall()
    {
        var sync = Source("Ledger.Android/VaultSync.cs");

        AssertGateComesFirst(sync, "public async Task<SecretsVaultPublishResult> PublishAsync", "accounts.GetInstitutionCredentialsAsync");
        AssertGateComesFirst(sync, "public async Task RestoreAsync", "vault.LoadAsync");
    }

    private static void AssertGateComesFirst(string source, string method, string firstRealWork)
    {
        var start = source.IndexOf(method, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{method} was not found.");
        var gate = source.IndexOf("InstallRoleGate.MayRunPlaidWork(roles.Current)", start, StringComparison.Ordinal);
        var work = source.IndexOf(firstRealWork, start, StringComparison.Ordinal);
        Assert.True(gate >= 0 && gate < work, $"{method} does real work before asking whether this install may.");
    }

    [Theory]
    [InlineData("Ledger.Android/RefreshWorker.cs")]
    [InlineData("Ledger.Android/WorksheetDeliveryWorker.cs")]
    public void TheBackgroundWorkersDoNothingOnHerInstall(string path)
    {
        var worker = Source(path);

        var gate = worker.IndexOf("InstallRoleGate.MayRunPlaidWork(", StringComparison.Ordinal);
        var configured = worker.IndexOf("SettingsStore.IsConfigured", StringComparison.Ordinal);
        Assert.True(gate >= 0, $"{path} does not consult the role before doing Plaid work.");
        Assert.True(gate < configured, $"{path} checks the role only after it has started on the credentials.");
    }

    // ---- R-G4 (wiring): the vault Plaid section is only ever written through the merge ----

    [Fact]
    public void VaultSyncWritesThePlaidSectionOnlyThroughTheMerge()
    {
        var sync = Source("Ledger.Android/VaultSync.cs");

        Assert.Contains("PlaidVaultMerge.Apply(", sync, StringComparison.Ordinal);
        Assert.DoesNotContain("new PlaidSecrets(", sync, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveWritesTheVaultOnlyWhenThisInstallMayTouchPlaid()
    {
        var setup = Source("Ledger.Android/Pages/SetupPage.xaml.cs");

        Assert.Matches(@"if \(InstallRoleGate\.MayRunPlaidWork\(role\)\)\s*\{\s*await PublishToVaultAsync\([a-z]*\);", setup);
    }

    // ---- one source of truth for the role ----

    [Theory]
    [InlineData("Ledger.Android/Pages/MainPage.xaml.cs")]
    [InlineData("Ledger.Android/Pages/SnowballPage.xaml.cs")]
    [InlineData("Ledger.Android/Pages/BudgetPage.xaml.cs")]
    [InlineData("Ledger.Android/Pages/SavingsPage.xaml.cs")]
    [InlineData("Ledger.Android/Pages/SetupPage.xaml.cs")]
    public void NoPageDerivesTheRoleItself(string path)
    {
        var page = Source(path);

        Assert.DoesNotContain("InstallRoleResolver", page, StringComparison.Ordinal);
        Assert.Contains("InstallRoleService", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingTheAccountOrThePassphraseForgetsTheRememberedRole()
    {
        var store = Source("Ledger.Android/SettingsStore.cs");

        Assert.Contains("ForgetInstallRoleIfChanged(Keys.VaultPassphrase", store, StringComparison.Ordinal);
        Assert.Contains("ForgetInstallRoleIfChanged(Keys.GoogleAccountEmail", store, StringComparison.Ordinal);
    }

    // ---- her Accounts carries no pinned "Add an account" bar ----

    [Fact]
    public void HerAccountsClearsThePinnedBar()
    {
        var main = Source("Ledger.Android/Pages/MainPage.xaml.cs");

        var branch = Regex.Match(main, @"if \(household\.IsHers\)\s*\{(?<body>.*?)return;", RegexOptions.Singleline);
        Assert.True(branch.Success, "MainPage no longer has a household branch in OnAppearing.");
        Assert.Contains("SetBar();", branch.Groups["body"].Value, StringComparison.Ordinal);
    }

    // ---- helpers ----

    private static string Source(string ledgerRelativePath)
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), ledgerRelativePath);
        Assert.True(File.Exists(path), $"Expected file not found: {ledgerRelativePath}");
        return File.ReadAllText(path);
    }
}
