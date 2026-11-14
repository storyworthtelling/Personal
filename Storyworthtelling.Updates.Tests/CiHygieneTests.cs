using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Storyworthtelling.Updates.Tests;

/// <summary>
/// CI minutes are the budget (efficiency audit §19–22, audit 13 B). These hold what the workflows were changed to do:
/// a signed build only on main or by hand, NuGet and the Android workloads restored from a cache, the FocusPhone
/// tests run once, the shared libraries tested only where a change reaches them, the log-loop guard run once per
/// change, and every workflow's path filter covering every project it builds or tests.
/// </summary>
public class CiHygieneTests
{
    private const string MainOrByHand = "if: github.ref == 'refs/heads/main' || github.event_name == 'workflow_dispatch'";

    [Fact]
    public void EveryAppWorkflowSignsOnlyOnMainOrByHandAndCancelsAStaleBranchRun()
    {
        var wrong = AppWorkflows()
            .Where(path => !File.ReadAllText(path).Contains(MainOrByHand, StringComparison.Ordinal)
                || !File.ReadAllText(path).Contains("concurrency:", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(wrong.Count == 0, "Signs an APK on every branch push, or lets a stale run go on:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void EveryWorkflowSetsUpDotnetThroughTheCachedAction()
    {
        var direct = Workflows()
            .Where(path => File.ReadAllText(path).Contains("actions/setup-dotnet@", StringComparison.Ordinal)
                || Regex.IsMatch(File.ReadAllText(path), @"run:\s*dotnet workload install"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(direct.Count == 0, "Restores NuGet and installs the workload from nothing on every run; use ./.github/actions/dotnet:\n" + string.Join("\n", direct));

        var action = File.ReadAllText(RepoFile(".github/actions/dotnet/action.yml"));
        Assert.Contains("actions/cache@", action, StringComparison.Ordinal);
        Assert.Contains(".nuget/packages", action, StringComparison.Ordinal);
        Assert.Contains("workload", action, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFocusPhoneTestsRunInOneWorkflow()
    {
        // The whole suite on a branch push, that is (the log-loop guard runs only its contract tests). main's build jobs run the suite inside the build, so an APK is
        // never published whose tests were skipped; those workflows no longer run on branches at all.
        var running = Workflows()
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"dotnet test FocusPhone/Codex\.Tests/Codex\.Tests\.csproj[ \t]*\r?\n")
                && File.ReadAllText(path).Contains("'claude/**'", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(["test-focusphone.yml"], running);
    }

    [Fact]
    public void FolioDoesNotRerunTheSharedLibrariesTests()
    {
        var folio = File.ReadAllText(RepoFile(".github/workflows/build-storyboarder.yml"));

        Assert.DoesNotContain("Storyworthtelling.Voice.Tests", folio, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyworthtelling.Remarkable.Tests", folio, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSharedLibrariesAreTestedOnlyWhereAChangeReachesThem()
    {
        var workflow = File.ReadAllText(RepoFile(".github/workflows/build-shared-libs.yml"));

        Assert.Contains("matrix:", workflow, StringComparison.Ordinal);
        Assert.Contains(".github/scripts/affected-tests.py", workflow, StringComparison.Ordinal);
        Assert.True(File.Exists(RepoFile(".github/scripts/affected-tests.py")));
    }

    [Fact]
    public void TheLogLoopGuardRunsOncePerChange()
    {
        var guard = File.ReadAllText(RepoFile(".github/workflows/log-loop-guard.yml"));

        // A push with a PR open steps aside for the PR's run, which is the one branch protection reads.
        Assert.Contains("gh pr list", guard, StringComparison.Ordinal);
        Assert.Contains("needs: covered", guard, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: true", guard, StringComparison.Ordinal);
    }

    // Orphan audit §46: the icon generator ran in CI without its tests, and nothing ran the seal's.
    [Fact]
    public void EveryToolsTestProjectIsRunByAWorkflow()
    {
        var all = string.Join("\n", Workflows().Select(File.ReadAllText));
        var missing = Directory.EnumerateDirectories(RepoFile("Tools"), "*.Tests")
            .Select(dir => $"Tools/{Path.GetFileName(dir)}/{Path.GetFileName(dir)}.csproj")
            .Where(project => File.Exists(RepoFile(project)) && !all.Contains("dotnet test " + project, StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0, "A tool's tests that no workflow runs:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryPathFilterCoversEveryProjectItsWorkflowBuilds()
    {
        var gaps = new List<string>();
        foreach (var path in Workflows())
        {
            var yaml = File.ReadAllText(path);
            var filters = PathFilters(yaml);
            if (filters.Count == 0)
            {
                continue;
            }

            var roots = Regex.Matches(yaml, @"[\w./-]+\.csproj").Select(m => m.Value).Distinct(StringComparer.Ordinal);
            foreach (var project in roots.SelectMany(Closure).Distinct(StringComparer.Ordinal))
            {
                var folder = Path.GetDirectoryName(project)!.Replace('\\', '/');
                if (!filters.Any(filter => folder == filter || folder.StartsWith(filter + "/", StringComparison.Ordinal)))
                {
                    gaps.Add($"{Path.GetFileName(path)}: {folder}");
                }
            }
        }

        Assert.True(gaps.Count == 0, "A change here runs none of the tests that read it:\n" + string.Join("\n", gaps.Distinct()));
    }

    // The folders a workflow's push filter names ('X/**'), relative to the repository.
    private static List<string> PathFilters(string yaml) =>
        Regex.Matches(yaml, @"^\s*-\s*'([^'*]+)/\*\*'", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();

    // A project and every project it reaches through ProjectReference, as repository-relative paths.
    private static IEnumerable<string> Closure(string project)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([project]);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            var full = RepoFile(next);
            if (!File.Exists(full) || !seen.Add(next))
            {
                continue;
            }

            foreach (var reference in XDocument.Load(full).Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value.Replace('\\', '/');
                if (include is null)
                {
                    continue;
                }
                var resolved = Path.GetRelativePath(RepoRoot(), Path.GetFullPath(Path.Combine(Path.GetDirectoryName(full)!, include)));
                pending.Push(resolved.Replace('\\', '/'));
            }
        }
        return seen;
    }

    private static IEnumerable<string> AppWorkflows() =>
        Workflows().Where(path => File.ReadAllText(path).Contains("dotnet publish", StringComparison.Ordinal));

    private static IEnumerable<string> Workflows() =>
        Directory.EnumerateFiles(RepoFile(".github/workflows"), "*.yml").OrderBy(f => f, StringComparer.Ordinal);

    private static string RepoFile(string relative) =>
        Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }
        return directory!.FullName;
    }
}
