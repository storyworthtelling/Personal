using DailyLoader.Domain.Identity;
using Xunit;

namespace DailyLoader.Tests.Guard;

/// <summary>
/// The in-place-upgrade guard for the Foreword (P9) rename. DailyLoader becomes Foreword
/// — folders, namespaces, project names and the on-device label all move — and an installed
/// phone must upgrade in place: no uninstall, no re-entering credentials, the device-owner
/// update channel still recognising it, and the scheduled delivery still firing.
///
/// <para>A handful of identifiers make that true, and every one of them is invisible: nothing
/// fails at build time if they move, and nothing fails loudly at run time either. The app just
/// quietly stops delivering, or comes up with empty settings. These tests fail instead.</para>
///
/// <para>They read the repository by content rather than by project or namespace name, so they
/// keep guarding the same invariants <i>after</i> the folders and namespaces have been renamed.</para>
/// </summary>
public class ForewordInvariantsTests
{
    // ---- G1: the package id ----

    /// <summary>
    /// A device recognises an update only when the package name is unchanged, and the
    /// device-owner update channel installs by package id. The name says DailyLoader and
    /// always will: it is the identity of the installed app, not a description of it.
    /// </summary>
    [Fact]
    public void ApplicationIdIsFrozen()
    {
        Assert.Equal("com.storyworthtelling.dailyloader", ForewordIdentity.ApplicationId);
    }

    [Fact]
    public void ExactlyOneProjectDeclaresTheApplicationId()
    {
        var declaring = Directory.EnumerateFiles(RepoRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file)
                .Contains($"<ApplicationId>{ForewordIdentity.ApplicationId}</ApplicationId>", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        Assert.Single(declaring);
    }

    /// <summary>
    /// The id must live in one constant. P8 found the Plate id copied into five files with only
    /// one of them guarded, so four could drift in silence; this is that lesson applied before
    /// the same thing happens here.
    /// </summary>
    [Fact]
    public void TheApplicationIdIsNotCopiedAroundTheCode()
    {
        var stray = RepoFiles("*.cs")
            .Where(file => File.ReadAllText(file)
                .Contains(ForewordIdentity.ApplicationId, StringComparison.Ordinal))
            .Select(Relative)
            .Where(path => !path.EndsWith("ForewordIdentity.cs", StringComparison.Ordinal)
                && !path.EndsWith("ForewordInvariantsTests.cs", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(stray.Count == 0,
            "The application id is copied instead of referenced. Every one of these should use "
            + $"ForewordIdentity.ApplicationId:\n{string.Join("\n", stray)}");
    }

    // ---- G2: the worker, which WorkManager persists by class name ----

    /// <summary>
    /// This is the one that would silently stop the mornings. <c>Schedule</c> enqueues unique
    /// periodic work with <c>ExistingPeriodicWorkPolicy.Keep</c>, so an already-scheduled phone
    /// keeps the row it has — and that row names the worker's <b>Java</b> class. Rename the
    /// namespace without pinning the registration and WorkManager wakes up, fails to instantiate
    /// a class that no longer exists, and nothing is delivered again. No crash the owner sees,
    /// no log they can reach.
    ///
    /// <para>So the registered name keeps <c>dailyloader</c> in it forever, even though the code
    /// around it says Foreword. It is an address, not a description.</para>
    /// </summary>
    [Fact]
    public void TheDeliveryWorkerKeepsItsRegisteredJavaName()
    {
        var worker = ComponentSource("DeliveryWorker");

        // Built from the frozen constant rather than a literal, so it cannot become a stray
        // copy — ApplicationIdIsFrozen pins the constant's value, and this pins what is
        // appended to it, which together pin the full Java name.
        Assert.Contains("Register(ForewordIdentity.ApplicationId + \".DeliveryWorker\")",
            worker, StringComparison.Ordinal);
    }

    /// <summary>
    /// The unique work name is persisted alongside the class name and is just as load-bearing:
    /// change it and the old work is never replaced, so the phone runs two schedules or none.
    /// It says "crossword" because that is what the app delivered first.
    /// </summary>
    [Fact]
    public void TheUniqueWorkNameIsFrozen()
    {
        Assert.Contains("\"crossword-delivery\"", ComponentSource("DeliveryWorker"), StringComparison.Ordinal);
    }

    // ---- G3: the launcher activity ----

    /// <summary>
    /// Without an explicit registration, .NET for Android generates the activity's Java name from
    /// the assembly and namespace, so the rename moves it. The manifest is regenerated each build
    /// and the launcher resolves MAIN/LAUNCHER at launch time, so this is far less dangerous than
    /// the worker — but pinning it costs one attribute and makes the name a decision rather than
    /// a side effect of where the class happens to live.
    /// </summary>
    [Fact]
    public void TheLauncherActivityHasAnExplicitRegisteredName()
    {
        var activity = ComponentSource("MainActivity");

        Assert.Contains("Register(ForewordIdentity.ApplicationId + \".MainActivity\")",
            activity, StringComparison.Ordinal);
    }

    // ---- G4: the stored credentials ----

    /// <summary>
    /// All nine preference keys, frozen. They used to be <c>nameof(TheProperty)</c>, which reads
    /// as tidy and is a trap: renaming the property renames the key, and the stored value is not
    /// migrated or reported — it is simply not found, and the app comes up as though it had never
    /// been configured. The vault passphrase is the worst of them, because it is the one secret
    /// the vault cannot restore.
    /// </summary>
    [Fact]
    public void EveryPreferenceKeyIsFrozen()
    {
        Assert.Equal(
        [
            "NytSessionCookie",
            "NytApiKey",
            "RemarkableDeviceToken",
            "GoogleAccountEmail",
            "AnthropicApiKey",
            "CalendarMappingJson",
            "VaultPassphrase",
            "LastDeliveryOutcome",
            "LastDeliveryAtUtc",
        ],
            PreferenceKeys.All);
    }

    [Fact]
    public void NoPreferenceKeyIsDerivedFromAnIdentifier()
    {
        var store = RepoFiles("*.cs").Single(f => Path.GetFileName(f) == "CredentialStore.cs");

        Assert.DoesNotContain("Preferences.Get(nameof", File.ReadAllText(store), StringComparison.Ordinal);
        Assert.DoesNotContain("Preferences.Set(nameof", File.ReadAllText(store), StringComparison.Ordinal);
    }

    // ---- G15: the toolchain ----

    /// <summary>
    /// The runner's default JDK is 17. PR #161 fixed exactly this failure on Flyleaf after the
    /// runner inherited 17 and javac refused a class-file major 65 binding.
    /// </summary>
    [Fact]
    public void TheWorkflowPinsJdk21()
    {
        var workflow = File.ReadAllText(RepoFile(".github/workflows/build-android.yml"));

        Assert.Contains("setup-java", workflow, StringComparison.Ordinal);
        Assert.Contains("java-version: '21'", workflow, StringComparison.Ordinal);
    }

    // ---- helpers ----

    private static string ComponentSource(string typeName)
    {
        var file = RepoFiles("*.cs").SingleOrDefault(f => Path.GetFileName(f) == $"{typeName}.cs");
        Assert.True(file is not null, $"{typeName}.cs was not found anywhere in the repository.");
        return File.ReadAllText(file!);
    }

    /// <summary>
    /// Files belonging to <b>this app</b>, not the whole repository. Other suite apps have their
    /// own MainActivity.cs, and one FocusPhone test legitimately names this package id as sample
    /// data — scanning everything turned both into false failures.
    /// </summary>
    private static IEnumerable<string> RepoFiles(string pattern) => AppFiles(pattern);

    private static IEnumerable<string> AppFiles(string pattern) =>
        Directory.EnumerateFiles(AppRoot(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>
    /// Found by content rather than by folder name, so the guard still works after the folder is
    /// renamed from DailyLoader to Foreword: the app root is the parent of whichever head declares
    /// the frozen ApplicationId.
    /// </summary>
    private static string AppRoot()
    {
        var head = Directory.EnumerateFiles(RepoRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SingleOrDefault(f => File.ReadAllText(f)
                .Contains($"<ApplicationId>{ForewordIdentity.ApplicationId}</ApplicationId>", StringComparison.Ordinal));

        Assert.True(head is not null,
            $"No project declares <ApplicationId>{ForewordIdentity.ApplicationId}</ApplicationId>.");
        return Directory.GetParent(Path.GetDirectoryName(head!)!)!.FullName;
    }

    private static string RepoFile(string relative) => Path.Combine(RepoRoot(), relative);

    private static string Relative(string absolute) =>
        Path.GetRelativePath(RepoRoot(), absolute).Replace(Path.DirectorySeparatorChar, '/');

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
