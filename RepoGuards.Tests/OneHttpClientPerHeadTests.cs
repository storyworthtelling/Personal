using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// A native head takes its HttpClient from <c>Codex.Ui.Http.HeadHttp</c> (audit 01 §19). Score built a new client
/// per call 21 times, some on the default handler every other comment in the repo says must be explicit, and the
/// timeouts differed site by site. The helper keeps one client per timeout for the process, always on Android's
/// own message handler.
/// </summary>
public class OneHttpClientPerHeadTests
{
    private static readonly string[] Heads =
    [
        "FocusPhone/Post.Android", "FocusPhone/Flyleaf.Android", "FocusPhone/Score.Android", "FocusPhone/Plate.Android",
        "FocusPhone/Codex.Ui.Android", "Pulp/Pulp.Android", "Constitution/Constitution.Android",
    ];

    private const string Helper = "FocusPhone/Codex.Ui.Android/Http/HeadHttp.cs";

    [Fact]
    public void NoNativeHeadBuildsItsOwnHttpClient()
    {
        var found = Heads
            .SelectMany(head => Directory.EnumerateFiles(Path.Combine(GuardScan.RepoRoot(), head), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(path => (path, relative: Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/')))
            .Where(file => file.relative != Helper)
            .SelectMany(file => File.ReadLines(file.path).Select((line, index) => (file.relative, line, index)))
            .Where(hit => Regex.IsMatch(hit.line, @"new HttpClient\(|HttpClient\s+\w+\s*=\s*new\(\)"))
            .Select(hit => $"{hit.relative}:{hit.index + 1}")
            .ToList();

        Assert.True(found.Count == 0, "Take the client from Codex.Ui.Http.HeadHttp:\n" + string.Join("\n", found));
    }

    [Fact]
    public void TheHelperUsesAndroidsOwnHandler()
    {
        var text = File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), Helper));
        Assert.Contains("new AndroidMessageHandler()", text);
        Assert.DoesNotContain("new HttpClient()", text);
    }

    [Fact]
    public void NoSharedClientIsDisposedOrHasItsDefaultsChanged()
    {
        // A client handed out to the whole process must not be disposed by one caller or given another's headers.
        var found = Heads
            .SelectMany(head => Directory.EnumerateFiles(Path.Combine(GuardScan.RepoRoot(), head), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(hit => Regex.IsMatch(hit.line, @"using\s+var\s+\w+\s*=\s*HeadHttp\.|HeadHttp\.\w+(\([^)]*\))?\.(DefaultRequestHeaders|BaseAddress|Timeout)"))
            .Select(hit => $"{Path.GetRelativePath(GuardScan.RepoRoot(), hit.path)}:{hit.index + 1}")
            .ToList();

        Assert.True(found.Count == 0, "A shared client is neither disposed nor reconfigured:\n" + string.Join("\n", found));
    }
}
