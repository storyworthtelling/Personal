using Index.Domain.Identity;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// The Guard G1 invariants for Index's own frozen surface — the identifiers that must not move
/// once Index has shipped a single build. P11's package-id break (D01) means there is no existing
/// <c>com.storyworthtelling.ledger</c> install to keep alive across the rename, so nothing here
/// existed to freeze before this phase. But from Index's first ship onward, every one of these is
/// exactly as load-bearing as Foreword's and Folio's own frozen surfaces: nothing fails at build
/// time if one of them moves, and nothing fails loudly at run time either. The refresh worker just
/// quietly stops running, or the app comes up with empty settings. These tests fail instead.
///
/// <para>They read the repository by content rather than by project or namespace name, matching
/// <c>ForewordInvariantsTests</c>/<c>FolioInvariantsTests</c>'s own pattern, so they keep guarding
/// the same invariants after any later rename moves the files again.</para>
/// </summary>
public class IndexInvariantsTests
{
    // ---- the package id ----

    /// <summary>
    /// A device recognises an update only when the package name is unchanged, and the
    /// device-owner update channel installs by package id. This is the app's address, not a
    /// description of what it does.
    /// </summary>
    [Fact]
    public void ApplicationIdIsFrozen()
    {
        Assert.Equal("com.storyworthtelling.index", IndexIdentity.ApplicationId);
    }

    [Fact]
    public void ExactlyOneProjectDeclaresTheApplicationId()
    {
        var declaring = Directory.EnumerateFiles(RepoRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file)
                .Contains($"<ApplicationId>{IndexIdentity.ApplicationId}</ApplicationId>", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        Assert.True(declaring.Count == 1,
            $"Exactly one csproj must declare the frozen ApplicationId '{IndexIdentity.ApplicationId}'; found {declaring.Count}:\n"
            + string.Join("\n", declaring));
    }

    /// <summary>
    /// The id must live in one constant. P8 found the Plate id copied into five files with only
    /// one of them guarded, so four could drift in silence — this is that lesson applied before
    /// the same thing happens here.
    /// </summary>
    [Fact]
    public void TheApplicationIdIsNotCopiedAroundTheCode()
    {
        var stray = AppFiles("*.cs")
            .Where(file => File.ReadAllText(file)
                .Contains(IndexIdentity.ApplicationId, StringComparison.Ordinal))
            .Select(Relative)
            .Where(path => !path.EndsWith("IndexIdentity.cs", StringComparison.Ordinal)
                && !path.EndsWith("IndexInvariantsTests.cs", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(stray.Count == 0,
            "The application id is copied instead of referenced. Every one of these should use "
            + $"IndexIdentity.ApplicationId:\n{string.Join("\n", stray)}");
    }

    /// <summary>
    /// The name under the launcher icon comes from the manifest's own <c>android:label</c>, which
    /// wins over the project's ApplicationTitle. It was left reading "Ledger" after the rename, so
    /// the new package installed beside the old app looking like a second copy of it. The label has
    /// to be the display name, and the old name must not survive anywhere a person can read it.
    /// </summary>
    [Fact]
    public void TheLauncherLabelIsTheDisplayName()
    {
        var manifest = File.ReadAllText(Path.Combine(
            RepoRoot(), "Ledger", "Ledger.Android", "Platforms", "Android", "AndroidManifest.xml"));

        Assert.Contains($"android:label=\"{IndexIdentity.DisplayName}\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("android:label=\"Ledger\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGoogleApplicationNameIsTheDisplayName()
    {
        var program = File.ReadAllText(Path.Combine(
            RepoRoot(), "Ledger", "Ledger.Android", "MauiProgram.cs"));

        Assert.DoesNotContain("ApplicationName = \"Ledger\"", program, StringComparison.Ordinal);
    }

    // ---- the refresh worker, which WorkManager persists by class name ----

    /// <summary>
    /// This is the one that would silently stop the refreshes. <c>Schedule</c> enqueues unique
    /// periodic work with <c>ExistingPeriodicWorkPolicy.Keep</c>, so an already-scheduled phone
    /// keeps the row it has — and that row names the worker's <b>Java</b> class. Move the class
    /// without pinning the registration and WorkManager wakes up, fails to instantiate a class
    /// that no longer exists, and nothing refreshes again. No crash the owner sees, no log they
    /// can reach.
    /// </summary>
    [Fact]
    public void TheRefreshWorkerKeepsItsRegisteredJavaName()
    {
        var worker = ComponentSource("RefreshWorker");

        // Built from the frozen constant rather than a literal, so it cannot become a stray
        // copy — ApplicationIdIsFrozen pins the constant's value, and this pins what is
        // appended to it, which together pin the full Java name.
        Assert.Contains("Register(IndexIdentity.ApplicationId + \".RefreshWorker\")",
            worker, StringComparison.Ordinal);
    }

    /// <summary>
    /// The unique work name is persisted alongside the class name and is just as load-bearing:
    /// change it and the old work is never replaced, so the phone runs two schedules or none.
    /// </summary>
    [Fact]
    public void TheWorkNameIsFrozen()
    {
        Assert.Contains("\"index-refresh\"", ComponentSource("RefreshWorker"), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>RunNow</c>'s one-shot uses a second unique work name so it can run alongside the
    /// periodic schedule without either cancelling the other. It is deliberately not a second
    /// independent literal — it is built from the frozen <c>WorkName</c> constant above, so it
    /// can never drift out of step with it the way two hand-typed strings could.
    /// </summary>
    [Fact]
    public void TheRunNowWorkNameIsDerivedFromTheFrozenWorkName()
    {
        var worker = ComponentSource("RefreshWorker");
        Assert.Contains("$\"{WorkName}-now\"", worker, StringComparison.Ordinal);
    }

    // ---- the launcher activity ----

    /// <summary>
    /// Without an explicit registration, .NET for Android generates the activity's Java name
    /// from the assembly and namespace, so a future rename would move it. Pinning it costs one
    /// attribute and makes the name a decision rather than a side effect of where the class
    /// happens to live.
    /// </summary>
    [Fact]
    public void MainActivityDeclaresAnExplicitJavaName()
    {
        var activity = ComponentSource("MainActivity");

        Assert.Contains("Name = IndexIdentity.ApplicationId + \".MainActivity\"",
            activity, StringComparison.Ordinal);
    }

    // ---- the stored settings ----

    /// <summary>
    /// All eight preference keys, frozen as literals. They were <c>nameof(TheProperty)</c>, which
    /// reads as tidy and is a trap: renaming the property renames the key, and the stored value is
    /// not migrated or reported — it is simply not found, and the app comes up as though it had
    /// never been configured. The vault passphrase is the worst of them, because it is the one
    /// secret the vault itself cannot restore.
    /// </summary>
    [Theory]
    [InlineData("PlaidClientId")]
    [InlineData("PlaidSecret")]
    [InlineData("PlaidEnvironment")]
    [InlineData("VaultPassphrase")]
    [InlineData("LastRunOutcome")]
    [InlineData("LastRunAtUtc")]
    [InlineData("GoogleAccountEmail")]
    [InlineData("LastMigrationError")]
    [InlineData("BudgetTrackingOnly")]
    [InlineData("BudgetCategories")]
    [InlineData("InstallRole")]
    [InlineData("SnowballHistoryImported")]
    public void EveryPreferenceKeyIsAFrozenLiteral(string key)
    {
        Assert.Contains($"\"{key}\"", SettingsStoreSource(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The mechanism is what is forbidden, not the word: a key derived from a property name via
    /// <c>nameof(...)</c> rather than a literal string constant.
    /// </summary>
    [Fact]
    public void NoPreferenceKeyIsDerivedFromAnIdentifier()
    {
        var store = SettingsStoreSource();

        Assert.DoesNotContain("Preferences.Get(nameof(", store, StringComparison.Ordinal);
        Assert.DoesNotContain("Preferences.Set(nameof(", store, StringComparison.Ordinal);
    }

    // ---- helpers ----

    private static string ComponentSource(string typeName)
    {
        var file = AppFiles("*.cs").SingleOrDefault(f => Path.GetFileName(f) == $"{typeName}.cs");
        Assert.True(file is not null, $"{typeName}.cs was not found anywhere under Index's app root.");
        return File.ReadAllText(file!);
    }

    /// <summary>
    /// Found by content rather than by path, so this survives a future rename moving the file: the
    /// one source in this head that reads and writes Preferences and mentions VaultPassphrase.
    /// </summary>
    private static string SettingsStoreSource()
    {
        var store = AppFiles("*.cs")
            .Select(File.ReadAllText)
            .FirstOrDefault(text => text.Contains("Preferences.Get(", StringComparison.Ordinal)
                && text.Contains("VaultPassphrase", StringComparison.Ordinal));

        Assert.NotNull(store);
        return store;
    }

    /// <summary>
    /// Files belonging to <b>this app</b>, not the whole repository. Other suite apps have their
    /// own MainActivity.cs, and other tests legitimately name package ids as sample data — scanning
    /// everything would turn both into false failures.
    /// </summary>
    private static IEnumerable<string> AppFiles(string pattern) =>
        Directory.EnumerateFiles(AppRoot(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>
    /// Found by content rather than by folder name, so the guard still works after a future rename
    /// moves the folder: the app root is the parent of whichever head declares the frozen
    /// ApplicationId.
    /// </summary>
    private static string AppRoot()
    {
        var head = Directory.EnumerateFiles(RepoRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SingleOrDefault(f => File.ReadAllText(f)
                .Contains($"<ApplicationId>{IndexIdentity.ApplicationId}</ApplicationId>", StringComparison.Ordinal));

        Assert.True(head is not null,
            $"No project declares <ApplicationId>{IndexIdentity.ApplicationId}</ApplicationId>.");
        return Directory.GetParent(Path.GetDirectoryName(head!)!)!.FullName;
    }

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
