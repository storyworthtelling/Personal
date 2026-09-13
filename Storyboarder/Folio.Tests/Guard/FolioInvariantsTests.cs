using Folio.Domain.Identity;
using Xunit;

namespace Folio.Tests.Guard;

/// <summary>
/// The in-place-upgrade guard for the Folio (P10) rename. The app renames its namespaces,
/// its solution, its context and its database file — but a device only upgrades in place,
/// keeps its schedule and keeps its settings, while a handful of identifiers stay
/// byte-identical. Every one of them is invisible: nothing fails at build time if it moves,
/// and nothing fails loudly at run time either. The app quietly stops updating, or stops
/// running its pipeline, or comes up with empty settings.
///
/// These assertions read the repo <b>by content</b> rather than by project or namespace name,
/// so they keep protecting the same invariants after the rename moves the files.
/// </summary>
public class FolioInvariantsTests
{
    private const string AppId = FolioIdentity.ApplicationId;

    // The twelve keys that were nameof()-derived — tidy-looking, and a trap: rename the
    // property and the stored value is not migrated, not reported, simply not found.
    private static readonly string[] FrozenPreferenceKeys =
    [
        "ClaudeApiKey",
        "GeminiApiKey",
        "VaultPassphrase",
        "ArchiveToDrive",
        "FreeLocalCopiesAfterArchive",
        "DeliverToRemarkable",
        "RemarkableDeviceToken",
        "GoogleAccountEmail",
        "LastRunOutcome",
        "LastRunAtUtc",
        "LastMigrationError",
        "CanonizerSchemaReset",
    ];

    // The update channel installs by package id. A changed id installs a SECOND app beside
    // the first, and the owner's books stay in the one that no longer updates.
    [Fact]
    public void ApplicationIdIsFrozen()
    {
        var declaring = RepoFiles("*.csproj")
            .Where(file => File.ReadAllText(file).Contains($"<ApplicationId>{AppId}</ApplicationId>", StringComparison.Ordinal))
            .ToList();

        Assert.True(declaring.Count == 1,
            $"Exactly one csproj must declare the frozen ApplicationId '{AppId}'; found {declaring.Count}:\n"
            + string.Join("\n", declaring.Select(Relative)));
    }

    /// <summary>
    /// The id must live in <b>one</b> constant. P8 found the Plate id copied into five files with
    /// only one of them guarded, so four could drift in silence; Foreword applied that lesson at P9.
    /// Folio carries the id in an attribute on the worker and an attribute on the launcher activity,
    /// both of which the rename moves — exactly the pair that must not be able to disagree.
    /// </summary>
    [Fact]
    public void TheApplicationIdIsNotCopiedAroundTheCode()
    {
        var stray = ProjectFiles()
            .Where(file => File.ReadAllText(file).Contains(AppId, StringComparison.Ordinal))
            .Select(Relative)
            .Where(path => !path.EndsWith("FolioIdentity.cs", StringComparison.Ordinal)
                && !path.EndsWith("FolioInvariantsTests.cs", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(stray.Count == 0,
            "The application id is copied instead of referenced. Every one of these should use "
            + $"FolioIdentity.ApplicationId:\n{string.Join("\n", stray)}");
    }

    // WorkManager instantiates the worker by class name, from a row persisted on the device.
    // Move the class without pinning this and the pipeline is never instantiated again.
    [Fact]
    public void WorkerRegisteredJavaNameIsFrozen()
    {
        Assert.True(
            RepoFiles("*.cs").Any(file => File.ReadAllText(file)
                .Contains("Register(FolioIdentity.ApplicationId + \".PipelineWorker\")", StringComparison.Ordinal)),
            "No source declares the frozen worker registration "
            + "[Register(FolioIdentity.ApplicationId + \".PipelineWorker\")]. "
            + "WorkManager instantiates by this name from a persisted row; without it the pipeline stops running.");
    }

    // EnqueueUniquePeriodicWork with Keep: a changed name leaves the old schedule running
    // AND adds a second one, so the phone runs two pipelines or none.
    [Fact]
    public void BothUniqueWorkNamesAreFrozen()
    {
        var source = HeadFiles()
            .Select(File.ReadAllText)
            .FirstOrDefault(text => text.Contains("EnqueueUniquePeriodicWork", StringComparison.Ordinal));

        Assert.NotNull(source);
        Assert.Contains("\"storyboarder-pipeline\"", source, StringComparison.Ordinal);
        Assert.Contains("-now", source, StringComparison.Ordinal);
    }

    // MainActivity had no declared Name, so its Java name was generated from the namespace —
    // which the rename moves. Home-screen shortcuts point at the old one.
    [Fact]
    public void MainActivityDeclaresAnExplicitJavaName()
    {
        var launcher = HeadFiles()
            .Select(File.ReadAllText)
            .FirstOrDefault(text => text.Contains("MainLauncher = true", StringComparison.Ordinal));

        Assert.NotNull(launcher);
        Assert.Contains("Name = FolioIdentity.ApplicationId + \".MainActivity\"", launcher, StringComparison.Ordinal);
    }

    // The stored keys must be literals. nameof() ties the key to the property name, so a
    // rename silently loses every setting the owner has entered.
    [Fact]
    public void PreferenceKeysAreFrozenLiterals()
    {
        var store = SettingsStoreSource();

        // The mechanism is what is forbidden, not the word: a key derived from a property name.
        Assert.DoesNotContain("Preferences.Get(nameof(", store, StringComparison.Ordinal);
        Assert.DoesNotContain("Preferences.Set(nameof(", store, StringComparison.Ordinal);
        foreach (var key in FrozenPreferenceKeys)
        {
            Assert.Contains($"\"{key}\"", store, StringComparison.Ordinal);
        }
    }

    // The user-facing name is the one thing the rename is for.
    [Fact]
    public void ApplicationTitleIsFolio()
    {
        Assert.True(
            RepoFiles("*.csproj").Any(file => File.ReadAllText(file)
                .Contains("<ApplicationTitle>Folio</ApplicationTitle>", StringComparison.Ordinal)),
            "No csproj declares <ApplicationTitle>Folio</ApplicationTitle> — the on-device label is still the old name.");
    }

    internal static string SettingsStoreSource()
    {
        // Found by content, not by path: the file in this head that reads and writes its preferences.
        var store = HeadFiles()
            .Select(File.ReadAllText)
            .FirstOrDefault(text => text.Contains("Preferences.Get(", StringComparison.Ordinal)
                                    && text.Contains("VaultPassphrase", StringComparison.Ordinal));

        Assert.NotNull(store);
        return store;
    }

    /// <summary>
    /// The head's own sources, located from a frozen fact rather than from a path: the one csproj
    /// that declares the frozen ApplicationId. This survives the rename moving every folder, and it
    /// keeps the assertions off the four other Android heads in this repository — each of which has
    /// its own launcher activity, its own WorkManager worker and its own preferences store.
    /// </summary>
    internal static IEnumerable<string> HeadFiles()
    {
        var head = RepoFiles("*.csproj")
            .FirstOrDefault(file => File.ReadAllText(file)
                .Contains($"<ApplicationId>{AppId}</ApplicationId>", StringComparison.Ordinal));

        Assert.True(head is not null, $"No csproj declares the frozen ApplicationId '{AppId}'.");

        return Directory.EnumerateFiles(Path.GetDirectoryName(head)!, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"));
    }

    /// <summary>
    /// Every source file in <b>this app's own</b> projects — the head, Domain, Infrastructure and the
    /// tests — found from the head's folder rather than by name, so the rename cannot move it.
    ///
    /// <para>Scoping matters here. Other projects in this monorepo legitimately name Folio's package
    /// id: the Flyleaf launcher categorises it, and the suite's version-band table lists it. Those are
    /// references to the app from outside, not copies of the constant inside it, and a guard that
    /// flagged them would be reporting someone else's business as this app's defect.</para>
    /// </summary>
    internal static IEnumerable<string> ProjectFiles()
    {
        var head = RepoFiles("*.csproj")
            .FirstOrDefault(file => File.ReadAllText(file)
                .Contains($"<ApplicationId>{AppId}</ApplicationId>", StringComparison.Ordinal));

        Assert.True(head is not null, $"No csproj declares the frozen ApplicationId '{AppId}'.");

        var projectRoot = Directory.GetParent(Path.GetDirectoryName(head!)!)!.FullName;

        return Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"));
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".github")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repo root (.github) above " + AppContext.BaseDirectory);
    }

    internal static string Relative(string file) => Path.GetRelativePath(RepoRoot(), file);

    internal static IEnumerable<string> RepoFiles(string pattern)
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
