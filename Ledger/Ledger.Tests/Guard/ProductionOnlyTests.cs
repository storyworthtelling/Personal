using Index.Infrastructure.Configuration;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Guard G17 (D144, plan addendum I5): Index knows only Plaid's <b>production</b> environment.
/// There is no environment control anywhere in the app — Ledger's sandbox/production picker
/// existed for development; Index removes it entirely, because a stray "sandbox" is not a visible
/// failure, it is a bank connection that looks and behaves exactly like a real one. The one place
/// this could quietly come back is a vault written by the old Ledger app: <c>PlaidSecrets.Environment</c>
/// stays on the wire for compatibility (D144), so it must never be read back into anything Index acts on.
/// </summary>
public class ProductionOnlyTests
{
    private const string MauiProgramPath = "Ledger/Ledger.Android/MauiProgram.cs";
    private const string SetupPageXamlPath = "Ledger/Ledger.Android/Pages/SetupPage.xaml";
    private const string SetupPageCodeBehindPath = "Ledger/Ledger.Android/Pages/SetupPage.xaml.cs";
    private const string VaultSyncPath = "Ledger/Ledger.Android/VaultSync.cs";

    // ---- PlaidOptions itself resolves to production, with nothing able to override it ----

    [Fact]
    public void PlaidOptionsResolvesToProductionWithNoEnvironmentSet()
    {
        var options = new PlaidOptions();

        Assert.Equal("production", options.Environment);
        Assert.Equal("https://production.plaid.com", options.BaseUrl);
    }

    // ---- the wiring in MauiProgram.cs never lets a stored Preferences value pick the host ----

    /// <summary>
    /// MauiProgram.cs is Android-only and cannot be constructed from this desktop-TFM test project,
    /// so this reads the actual wiring by source text — the same pattern <c>IndexInvariantsTests</c>
    /// already uses for content this project cannot execute. If the wiring ever again reads
    /// <c>SettingsStore.PlaidEnvironment</c> into <c>PlaidOptions.Environment</c>, a value restored
    /// from an old vault (or typed into Preferences by hand) would decide which Plaid host a real
    /// production build talks to.
    /// </summary>
    [Fact]
    public void MauiProgramDoesNotWireEnvironmentFromSettingsStore()
    {
        var source = SourceAt(MauiProgramPath);

        Assert.DoesNotContain("SettingsStore.PlaidEnvironment", source, StringComparison.Ordinal);
    }

    // ---- the picker is gone from the screen, not merely hidden ----

    [Fact]
    public void SetupPageXamlHasNoEnvironmentPicker()
    {
        var xaml = SourceAt(SetupPageXamlPath);

        Assert.DoesNotContain("EnvironmentPicker", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("sandbox", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetupPageCodeBehindDoesNotTouchAnEnvironmentPicker()
    {
        var codeBehind = SourceAt(SetupPageCodeBehindPath);

        Assert.DoesNotContain("EnvironmentPicker", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("sandbox", codeBehind, StringComparison.OrdinalIgnoreCase);
    }

    // ---- restoring an old vault can never bring "sandbox" back ----

    [Fact]
    public void VaultRestoreNeverSetsPlaidEnvironmentFromTheLoadedVault()
    {
        var source = SourceAt(VaultSyncPath);

        Assert.DoesNotContain("SettingsStore.PlaidEnvironment =", source, StringComparison.Ordinal);
    }

    // ---- helpers ----

    private static string SourceAt(string repoRelativePath)
    {
        var path = Path.Combine(RepoRoot(), repoRelativePath);
        Assert.True(File.Exists(path), $"Expected file not found: {repoRelativePath}");
        return File.ReadAllText(path);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
