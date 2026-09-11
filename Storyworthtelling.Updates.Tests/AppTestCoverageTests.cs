using System.Text.RegularExpressions;

namespace Storyworthtelling.Updates.Tests;

/// <summary>
/// Every app workflow that builds a head also <b>runs that app's tests</b>.
///
/// <para>A workflow that only builds reports green for an APK that compiles, which says nothing
/// about whether the app works — and it says it in exactly the tone of a workflow that checked. That
/// is worse than no check at all: a guard nobody runs is a guard everybody believes.</para>
///
/// <para>Foreword is why this exists. <c>build-android.yml</c> built the APK, read the icon back out
/// of it, and published it to the rolling release — and never ran a single test. Its whole guard
/// suite (frozen package id, the worker's registered name, the vault's wire key, the XAML checks,
/// the design tokens) had never been enforced by anything but somebody remembering to run it
/// locally.</para>
///
/// <para>Like the shared-library guard beside it, this lives in a project that <i>is</i> on a
/// workflow that runs — a guard that only fires once its own suite is wired up cannot catch the
/// omission it exists for.</para>
/// </summary>
public class AppTestCoverageTests
{
    [Fact]
    public void EveryAppWorkflowRunsItsAppsTests()
    {
        var missing = new List<string>();

        foreach (var workflow in Directory.EnumerateFiles(
            RepoFile(".github/workflows"), "build-*.yml").OrderBy(f => f, StringComparer.Ordinal))
        {
            var yaml = File.ReadAllText(workflow);

            // Only the workflows that build an app head. build-shared-libs has no head and is
            // already covered by SharedLibraryCoverageTests.
            if (!yaml.Contains("dotnet publish", StringComparison.Ordinal))
            {
                continue;
            }

            if (!Regex.IsMatch(yaml, @"dotnet test\s"))
            {
                missing.Add($"{Path.GetFileName(workflow)}: builds an APK and runs no tests");
            }
        }

        Assert.True(missing.Count == 0,
            "These report green for an APK that merely compiles, in the tone of a workflow that "
            + "checked something:\n" + string.Join("\n", missing));
    }

    private static string RepoFile(string relative) =>
        Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

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
