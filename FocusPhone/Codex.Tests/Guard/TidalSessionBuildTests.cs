using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// The playback service builds its TIDAL session in a fire-and-forget <c>Task.Run</c>. The token refresh
/// inside it throws when the phone is offline (the auth host does not resolve), and nothing awaited the task,
/// so the failure surfaced as an unobserved task exception and was recorded as a crash. The task's target
/// catches everything, logs one line and leaves the build free to run again later.
/// </summary>
public class TidalSessionBuildTests
{
    private static readonly string SourcePath = Path.Combine(
        RepoRoot(), "FocusPhone", "Score.Android", "PlaybackService.cs");

    [Fact]
    public void TheFireAndForgetBuildCatchesEverything()
    {
        var source = File.ReadAllText(SourcePath);

        var run = Regex.Match(source, @"tidalBuild\s*=\s*Task\.Run\(\s*\(\)\s*=>\s*(?<target>\w+)\(");
        Assert.True(run.Success, "the TIDAL build is no longer started with Task.Run");

        var target = run.Groups["target"].Value;
        var body = Regex.Match(
            source,
            $@"private\s+async\s+Task\s+{target}\s*\([^)]*\)\s*\{{\s*(?:const[^;]*;\s*)?try\s*\{{(?<rest>.*?)\n    \}}\n",
            RegexOptions.Singleline);
        Assert.True(body.Success, $"{target} does not open with a try around its whole body");
        Assert.Matches(new Regex(@"catch\s*\(\s*Exception\s+\w+\s*\)"), body.Groups["rest"].Value);
        Assert.Contains("HeadLog.Warn", body.Groups["rest"].Value);
    }

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
}
