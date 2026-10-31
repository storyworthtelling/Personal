using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Reads head source for the log loop's guards (LOG-LOOP-PLAN.md §4). The heads are net10.0-android and
/// cannot be loaded here, so — like the repo's other guards — these read the files by content.
/// </summary>
internal static class LogLoopSource
{
    public static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    public static string File(string relative) =>
        System.IO.File.ReadAllText(Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Every .cs file under a folder, bin and obj excluded, as (relative path, text).</summary>
    public static IReadOnlyList<(string Path, string Text)> Sources(string relativeFolder)
    {
        var root = RepoRoot();
        var folder = Path.Combine(root, relativeFolder.Replace('/', Path.DirectorySeparatorChar));
        var separator = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{separator}bin{separator}") && !p.Contains($"{separator}obj{separator}"))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => (Path.GetRelativePath(root, p).Replace('\\', '/'), System.IO.File.ReadAllText(p)))
            .ToList();
    }
}
