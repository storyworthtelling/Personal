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

        Assert.True(missing.Count == 0, "A tool’s tests that no workflow runs:\n" + string.Join("\n", missing));
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

    /// <summary>
    /// P14 W-3.1.1 (risk R1): a watch APK carries its phone app's package and a higher versionCode band, so the only thing
    /// that keeps it off the phone is its form. Every publish says which form it carries and the action checks it against
    /// each APK's own manifest; a workflow that builds a watch APK publishes it as a watch APK; and every watch app
    /// declares the watch feature, since one that forgot would read as a phone APK by every measure. The watch workflow
    /// is a later chunk: this holds now and bites when it lands (see <see cref="TheWatchRuleBites"/>).
    /// </summary>
    [Fact]
    public void EveryWatchApkSaysWatch()
    {
        var action = File.ReadAllText(RepoFile(".github/actions/publish-rolling-release/action.yml"));
        var script = File.ReadAllText(RepoFile(".github/scripts/stage-apks.py"));

        // The action asks for the form, stages every APK through the script, and never publishes without a sidecar.
        Assert.Matches(@"(?m)^  form:\s*\n(?:\s+.*\n)*?\s+required: true", action);
        Assert.Contains("stage-apks.py", action, StringComparison.Ordinal);
        Assert.Contains("--form", action, StringComparison.Ordinal);
        Assert.DoesNotContain("publishing without sidecars", action, StringComparison.Ordinal);

        // The script reads the feature the phone reads and names a watch asset the way the phone reads names.
        Assert.Contains($"\"{ApkForms.WatchFeature}\"", script, StringComparison.Ordinal);
        Assert.Contains($"\"{ApkForms.WatchAssetSuffix}\"", script, StringComparison.Ordinal);
        Assert.Contains($"\"{ApkForms.Field}\"", script, StringComparison.Ordinal);
        Assert.Contains($"\"{ApkForms.PhoneWord}\"", script, StringComparison.Ordinal);
        Assert.Contains($"\"{ApkForms.WatchWord}\"", script, StringComparison.Ordinal);

        var gaps = Workflows()
            .SelectMany(path => PublishFormGaps(Path.GetFileName(path), File.ReadAllText(path)))
            .Concat(WatchAppsWithoutTheFeature(RepoRoot()))
            .ToList();

        Assert.True(gaps.Count == 0, "A watch APK that could be published as a phone APK:\n" + string.Join("\n", gaps));
    }

    [Fact]
    public void TheWatchRuleBites()
    {
        const string Build = """
            name: Build watch
            jobs:
              build:
                steps:
                  - name: Build Dash
                    run: dotnet publish Watch/Dash.Wear/Dash.Wear.csproj -c Release
                  - name: Publish to rolling GitHub Release
                    if: github.ref == 'refs/heads/main'
                    uses: ./.github/actions/publish-rolling-release
                    with:
                      apks: Watch/Dash.Wear/bin/Release/net10.0-android/publish/*-Signed.apk
            """;

        Assert.NotEmpty(PublishFormGaps("build-watch.yml", Build));
        Assert.NotEmpty(PublishFormGaps("build-watch.yml", Build + "\n          form: phone\n"));
        Assert.Empty(PublishFormGaps("build-watch.yml", Build + "\n          form: watch\n"));
        Assert.NotEmpty(PublishFormGaps("build-post.yml", Build.Replace("Watch/Dash.Wear/Dash.Wear.csproj", "FocusPhone/Post.Android/Post.Android.csproj", StringComparison.Ordinal) + "\n          form: phone\n"));

        var root = Directory.CreateTempSubdirectory("watch-rule-").FullName;
        var dash = Directory.CreateDirectory(Path.Combine(root, "Watch", "Dash.Wear")).FullName;
        File.WriteAllText(Path.Combine(dash, "Dash.Wear.csproj"), "<Project><PropertyGroup><ApplicationId>com.example.dash</ApplicationId></PropertyGroup></Project>");
        Assert.NotEmpty(WatchAppsWithoutTheFeature(root));
        File.WriteAllText(Path.Combine(dash, "WatchFeature.cs"), "[assembly: Android.App.UsesFeature(\"android.hardware.type.watch\")]");
        Assert.Empty(WatchAppsWithoutTheFeature(root));
    }

    // Each call of the publish action names its form; a call that publishes from Watch/, and every call in a workflow that
    // builds a watch APK, says watch; a call that says watch is in a workflow that builds one.
    private static IEnumerable<string> PublishFormGaps(string name, string yaml)
    {
        var buildsWatch = Regex.IsMatch(yaml, @"dotnet (?:publish|build)\s+Watch/[\w./-]+\.csproj")
            || Regex.IsMatch(yaml, @"Tools/FaceStudio[^\n]*--apk");
        var steps = Regex.Split(yaml, @"(?m)^(?=\s*- (?:name|uses|run):)");
        foreach (var step in steps.Where(step => step.Contains("uses: ./.github/actions/publish-rolling-release", StringComparison.Ordinal)))
        {
            var form = Regex.Match(step, @"(?m)^\s+form:\s*['""]?(\w+)['""]?\s*$");
            var said = form.Success ? form.Groups[1].Value : null;
            if (said is not (ApkForms.PhoneWord or ApkForms.WatchWord))
            {
                yield return $"{name}: a publish that doesn't say its form (phone or watch)";
                continue;
            }

            var fromWatch = Regex.IsMatch(step, @"(?m)^\s+apks:[\s\S]*\bWatch/");
            if ((fromWatch || buildsWatch) && said != ApkForms.WatchWord)
            {
                yield return $"{name}: publishes a watch APK as {said}";
            }
            if (!buildsWatch && !fromWatch && said == ApkForms.WatchWord)
            {
                yield return $"{name}: says watch but builds no watch APK";
            }
        }
    }

    // Every app project under Watch/ (one with an ApplicationId) declares android.hardware.type.watch, in a manifest or
    // as a UsesFeature attribute, and so does the face's generated manifest.
    private static IEnumerable<string> WatchAppsWithoutTheFeature(string root)
    {
        var watch = Path.Combine(root, "Watch");
        if (!Directory.Exists(watch))
        {
            yield break;
        }

        var apps = Directory.EnumerateFiles(watch, "*.csproj", SearchOption.AllDirectories)
            .Where(project => File.ReadAllText(project).Contains("<ApplicationId>", StringComparison.Ordinal))
            .Select(Path.GetDirectoryName)
            .ToList();
        var face = Path.Combine(watch, "Point.Face");
        if (Directory.Exists(face))
        {
            apps.Add(face);
        }

        foreach (var app in apps)
        {
            var sources = Directory.EnumerateFiles(app!, "*", SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
            var declares = sources.Any(file =>
                (Path.GetFileName(file) == "AndroidManifest.xml"
                    && Regex.IsMatch(File.ReadAllText(file), @"<uses-feature\s+android:name=""android\.hardware\.type\.watch"""))
                || (file.EndsWith(".cs", StringComparison.Ordinal)
                    && Regex.IsMatch(File.ReadAllText(file), @"UsesFeature\(\s*""android\.hardware\.type\.watch""")));
            if (!declares)
            {
                yield return $"{Path.GetRelativePath(root, app!).Replace('\\', '/')}: declares no android.hardware.type.watch";
            }
        }
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
