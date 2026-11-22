using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Media3 allows one <c>SimpleCache</c> per folder per process: a second one over the same folder throws
/// <c>IllegalStateException</c> ("another SimpleCache instance uses the folder"), and a cache built and
/// dropped without <c>Release</c> still holds it. The TIDAL player factory built a fresh cache on every call,
/// and the service retried the build, so the second try crashed the app. Every cache in Score is opened once,
/// into a static field, and a TIDAL player that failed to build is not built again in the same process.
/// </summary>
public class ScoreSimpleCacheTests
{
    private static readonly string HeadPath = Path.Combine(RepoRoot(), "FocusPhone", "Score.Android");

    [Fact]
    public void EveryNewSimpleCacheIsAssignedOnceToAStaticField()
    {
        var found = 0;
        foreach (var path in Directory.EnumerateFiles(HeadPath, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var source = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(source, @"new\s+SimpleCache\s*\("))
            {
                found++;
                var before = source[..match.Index];
                var assignment = Regex.Match(before, @"(?<field>\w+)\s*\?\?=\s*$");
                Assert.True(assignment.Success, $"{Path.GetFileName(path)}: a SimpleCache built without ??=");

                var field = assignment.Groups["field"].Value;
                Assert.Matches(new Regex($@"private\s+static\s+SimpleCache\?\s+{field}\s*;"), source);
            }
        }

        Assert.True(found >= 2, "expected the downloads cache and the TIDAL cache");
    }

    [Fact]
    public void AFailedTidalPlayerIsNotBuiltAgainInTheSameProcess()
    {
        var factory = File.ReadAllText(Path.Combine(HeadPath, "TidalPlayerFactory.cs"));
        var service = File.ReadAllText(Path.Combine(HeadPath, "PlaybackService.cs"));

        Assert.Matches(new Regex(@"internal\s+static\s+bool\s+GaveUp\s*\{\s*get;\s*private\s+set;\s*\}"), factory);
        Assert.Matches(new Regex(@"GaveUp\s*=\s*true"), factory);
        Assert.Contains("TidalPlayerFactory.GaveUp", service);
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
