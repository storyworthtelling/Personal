using System.Text.RegularExpressions;

namespace Storyworthtelling.Updates.Tests;

/// <summary>
/// Every shared library that has tests is actually run by the shared-libraries workflow.
///
/// <para>The repo-root libraries belong to no app solution, which is the whole reason
/// <c>build-shared-libs.yml</c> exists: before it, a shared-lib change that no app workflow's path
/// filter covered went untested. That workflow names its test projects in a hand-written list and
/// filters on a hand-written set of paths, so the same gap reopens the moment a new shared library
/// is added and someone forgets — and forgetting is silent, because a workflow that does not run
/// reports nothing at all rather than reporting a failure.</para>
///
/// <para>This test lives here rather than beside the newest library on purpose: a guard that only
/// runs once its own project has been added to the list cannot catch the omission it exists
/// for.</para>
/// </summary>
public class SharedLibraryCoverageTests
{
    [Fact]
    public void EveryRootTestProjectIsRunByTheSharedLibrariesWorkflow()
    {
        var workflow = File.ReadAllText(RepoFile(".github/workflows/build-shared-libs.yml"));

        var missing = new List<string>();
        foreach (var project in RootTestProjects())
        {
            var name = Path.GetFileName(project);
            if (!workflow.Contains(name, StringComparison.Ordinal))
            {
                missing.Add($"{name}: never run — build-shared-libs.yml does not name it");
            }

            var library = Path.GetFileName(Path.GetDirectoryName(project)!).Replace(".Tests", string.Empty);
            if (!Regex.IsMatch(workflow, $@"- '{Regex.Escape(library)}/\*\*'"))
            {
                missing.Add($"{library}: changing it triggers nothing — no path filter covers it");
            }
        }

        Assert.NotEmpty(RootTestProjects());
        Assert.True(missing.Count == 0,
            "A shared library outside the workflow is not tested by anything, and says so by "
            + "staying silent:\n" + string.Join("\n", missing));
    }

    private static List<string> RootTestProjects() =>
        Directory.EnumerateDirectories(RepoRoot(), "Storyworthtelling.*.Tests")
            .Select(d => Path.Combine(d, Path.GetFileName(d) + ".csproj"))
            .Where(File.Exists)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

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
