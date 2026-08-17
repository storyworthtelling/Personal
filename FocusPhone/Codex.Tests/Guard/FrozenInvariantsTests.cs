using Codex.Domain.Policy;
using Codex.Domain.Updates;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// The in-place-upgrade guard for the Flyleaf (P5) rename pilot. The launcher head
/// rebrands FocusPhone.* → Codex.* and Launcher → Flyleaf, but an installed device
/// only upgrades in place — no uninstall, no re-pairing, the device-owner update
/// channel still recognizing it — while a handful of identifiers stay byte-identical.
/// These tests fail if any of them drift, so the rename can never silently break the
/// home role or the update channel. The assertions read the repo by content (not by
/// project/namespace names), so they keep protecting the same invariants after the
/// folders and namespaces are renamed.
/// </summary>
public class FrozenInvariantsTests
{
    // The app id and owner namespace are the load-bearing constants; a device
    // recognizes an update only when the package name is unchanged.
    [Fact]
    public void LauncherAppIdAndOwnerPrefixAreFrozen()
    {
        Assert.Equal("com.storyworthtelling.focusphone.launcher", PolicyContext.LauncherPackage);
        Assert.Equal("com.storyworthtelling.", PolicyContext.OwnerPackagePrefix);
        Assert.Equal("com.storyworthtelling.focusphone.communication", PolicyContext.CommunicationPackage);
        Assert.Equal("com.storyworthtelling.focusphone.camera", PolicyContext.CameraPackage);
        Assert.Equal("com.storyworthtelling.focusphone.audio", PolicyContext.AudioPackage);
    }

    // Exactly one head declares the frozen ApplicationId — the launcher. The rename
    // may move/rename the csproj, but the literal must survive it.
    [Fact]
    public void LauncherCsprojDeclaresFrozenApplicationId()
    {
        var declaring = EnumerateRepoFiles("*.csproj")
            .Where(file => File.ReadAllText(file)
                .Contains($"<ApplicationId>{PolicyContext.LauncherPackage}</ApplicationId>", StringComparison.Ordinal))
            .ToList();

        Assert.True(declaring.Count == 1,
            $"Exactly one csproj must declare the frozen launcher ApplicationId " +
            $"'{PolicyContext.LauncherPackage}'; found {declaring.Count}:\n" +
            string.Join("\n", declaring.Select(RepoRelative)));
    }

    // The home Activity must keep MainLauncher + CategoryHome, or Flyleaf stops being
    // the device home app after the upgrade.
    [Fact]
    public void HomeActivityKeepsLauncherAndHomeIntent()
    {
        var declaresHome = EnumerateRepoFiles("*.cs").Any(file =>
        {
            var source = File.ReadAllText(file);
            return source.Contains("MainLauncher = true", StringComparison.Ordinal)
                && source.Contains("Intent.CategoryHome", StringComparison.Ordinal);
        });

        Assert.True(declaresHome,
            "No Activity declares 'MainLauncher = true' with 'Intent.CategoryHome' — " +
            "the rebranded launcher would no longer register as the device home app.");
    }

    // The update channel stays the 'apps-latest' rolling release, matched by the owner
    // namespace (not by APK filename), so renaming workflows/assets can't break it.
    [Fact]
    public void UpdateChannelIsAppsLatestByOwnerNamespace()
    {
        var githubRoot = Path.Combine(RepoRoot(), ".github");
        var mentionsAppsLatest = Directory
            .EnumerateFiles(githubRoot, "*.yml", SearchOption.AllDirectories)
            .Any(file => File.ReadAllText(file).Contains("apps-latest", StringComparison.Ordinal));

        Assert.True(mentionsAppsLatest,
            "The 'apps-latest' rolling-release tag is gone from .github — the device-owner update channel would break.");

        // The install decision is package-id based: an owner app upgrades, a stray package is skipped.
        var upgraded = SiblingUpdatePlan.Decide(
            [new InstallCandidate(PolicyContext.LauncherPackage, 2)],
            new Dictionary<string, long> { [PolicyContext.LauncherPackage] = 1 });
        Assert.Single(upgraded);

        var skipped = SiblingUpdatePlan.Decide(
            [new InstallCandidate("com.example.other", 9)],
            new Dictionary<string, long>());
        Assert.Empty(skipped);
    }

    // The heads sign with the one shared repo keystore, so they share a single
    // signature the update channel accepts across the suite. Asserted by capability
    // (shared keystore + alias driving a signed Android build), not by head name, so
    // the rename can't spuriously trip it.
    [Fact]
    public void HeadsSignWithTheSharedKeystore()
    {
        var signingWorkflow = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), ".github", "workflows"), "*.yml")
            .Select(File.ReadAllText)
            .FirstOrDefault(text =>
                text.Contains("secrets.KEYSTORE_BASE64", StringComparison.Ordinal)
                && text.Contains("AndroidSigningKeyStore", StringComparison.Ordinal));

        Assert.True(signingWorkflow is not null,
            "No workflow signs an Android head with the shared secrets.KEYSTORE_BASE64 keystore.");
        Assert.Contains("secrets.KEY_ALIAS", signingWorkflow!, StringComparison.Ordinal);
    }

    // ---- repo helpers (root = the ancestor holding .github; survives the rename) ----

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".github")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repo root (.github) above " + AppContext.BaseDirectory);
    }

    private static string RepoRelative(string file) => Path.GetRelativePath(RepoRoot(), file);

    private static IEnumerable<string> EnumerateRepoFiles(string pattern)
    {
        foreach (var file in Directory.EnumerateFiles(RepoRoot(), pattern, SearchOption.AllDirectories))
        {
            var parts = file.Split(Path.DirectorySeparatorChar);
            if (parts.Any(part => part is "obj" or "bin" or ".git"))
            {
                continue;
            }
            yield return file;
        }
    }
}
