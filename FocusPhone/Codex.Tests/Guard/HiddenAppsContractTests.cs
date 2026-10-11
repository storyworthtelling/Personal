using System.Text.RegularExpressions;
using Codex.Domain.Apps;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Guard G17, Flyleaf's half: the contract Pulp sends hidden apps over (D8, D92, D93). The expected
/// values are typed out here on purpose, because the guard exists to notice them moving; Pulp's own
/// <c>HiddenAppsContractTests</c> reads <c>HiddenAppsHandoff.cs</c> by content, so the two halves
/// can only agree by both agreeing with this file's constants. Nothing fails at run time if they
/// drift: the broadcast is simply dropped and the streaming apps stay in the tray.
/// </summary>
public class HiddenAppsContractTests
{
    [Fact]
    public void TheWireNamesAreFrozen()
    {
        Assert.Equal("com.storyworthtelling.focusphone.launcher", HiddenAppsHandoff.Package);
        Assert.Equal("com.storyworthtelling.focusphone.launcher.HiddenAppsReceiver", HiddenAppsHandoff.Receiver);
        Assert.Equal("com.storyworthtelling.focusphone.launcher.action.HIDDEN_APPS", HiddenAppsHandoff.Action);
        Assert.Equal("com.storyworthtelling.focusphone.launcher.RECEIVE_HIDDEN", HiddenAppsHandoff.Permission);
        Assert.Equal("packages", HiddenAppsHandoff.PackagesExtra);
        Assert.Equal("hiddenApps", HiddenAppsHandoff.PreferencesName);
        Assert.Equal(PolicyContext.LauncherPackage, HiddenAppsHandoff.Package);
    }

    [Fact]
    public void ThePulpPackageIsFrozenAndEqualsPulpsOwnIdentity()
    {
        Assert.Equal("com.storyworthtelling.pulp", PolicyContext.PulpPackage);
        var identity = File.ReadAllText(Path.Combine(RepoRoot(), "Pulp", "Pulp.Domain", "PulpIdentity.cs"));
        var declared = Regex.Match(identity, @"public const string ApplicationId = ""([^""]*)""");
        Assert.True(declared.Success, "PulpIdentity.ApplicationId was not found.");
        Assert.Equal(declared.Groups[1].Value, PolicyContext.PulpPackage);
    }

    [Fact]
    public void TheReceiverIsRegisteredUnderTheContractsNames()
    {
        var source = ReceiverSource();
        Assert.Contains("Name = HiddenAppsHandoff.Receiver", source, StringComparison.Ordinal);
        Assert.Contains("Permission = HiddenAppsHandoff.Permission", source, StringComparison.Ordinal);
        Assert.Contains("Exported = true", source, StringComparison.Ordinal);
        Assert.Contains("[IntentFilter([HiddenAppsHandoff.Action])]", source, StringComparison.Ordinal);
        Assert.Contains("intent?.Action != HiddenAppsHandoff.Action", source, StringComparison.Ordinal);
        Assert.Contains("GetStringExtra(HiddenAppsHandoff.PackagesExtra)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A receiver naming a permission nobody declares is silently unenforceable: Android never grants
    /// it to anyone, Pulp included. The declaration is an assembly attribute at signature level.
    /// </summary>
    [Fact]
    public void ThePermissionIsDeclaredAtSignatureLevel()
    {
        var source = ReceiverSource();
        Assert.Matches(
            @"\[assembly: Permission\(Name = HiddenAppsHandoff\.Permission, ProtectionLevel = Protection\.Signature\)\]",
            source);
    }

    /// <summary>A garbled or missing payload must not clobber the stored set; only a valid list does.</summary>
    [Fact]
    public void TheReceiverStoresOnlyWhatDecodes()
    {
        var source = ReceiverSource();
        Assert.Contains("HiddenAppsCodec.TryDecode(", source, StringComparison.Ordinal);
        Assert.Contains("new HiddenAppsStore(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStoreUsesTheFixedPreferenceFile()
    {
        var store = File.ReadAllText(Path.Combine(RepoRoot(), "FocusPhone", "Flyleaf.Android", "HiddenAppsStore.cs"));
        Assert.Contains("HiddenAppsHandoff.PreferencesName", store, StringComparison.Ordinal);
        Assert.Contains("HiddenAppsCodec.Decode(", store, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShelfBuilderPassesTheStoredSetToTheTray()
    {
        var builder = File.ReadAllText(Path.Combine(RepoRoot(), "FocusPhone", "Flyleaf.Android", "ShelfSceneBuilder.cs"));
        Assert.Matches(@"AppTrayLayout\.Build\([^;]*new HiddenAppsStore\(context\)\.Load\(\)", builder);
    }

    private static string ReceiverSource()
    {
        var path = Path.Combine(RepoRoot(), "FocusPhone", "Flyleaf.Android", "HiddenAppsReceiver.cs");
        Assert.True(File.Exists(path), $"HiddenAppsReceiver.cs not found at {path}.");
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
