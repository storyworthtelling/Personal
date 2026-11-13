namespace RepoGuards.Tests;

/// <summary>
/// Efficiency audit §3: a vault or household read downloaded the whole file every time, even when Drive's revision
/// showed it had not changed. Every Drive file a head builds shares the process's read cache, so an unchanged file
/// is downloaded once.
/// </summary>
public class DriveReadsAreCachedTests
{
    [Fact]
    public void EveryHeadsDriveFileSharesTheReadCache()
    {
        var found = new List<string>();
        foreach (var file in Sources())
        {
            var text = File.ReadAllText(file);
            var at = 0;
            while ((at = text.IndexOf("new DriveTextFile(", at, StringComparison.Ordinal)) >= 0)
            {
                var call = Call(text, at + "new DriveTextFile(".Length);
                if (!call.Contains("DriveReadCache.Shared", StringComparison.Ordinal))
                {
                    found.Add($"{Relative(file)}:{text[..at].Count(c => c == '\n') + 1}");
                }
                at += call.Length;
            }
        }

        Assert.True(found.Count == 0, "Pass readCache: DriveReadCache.Shared:\n" + string.Join("\n", found));
    }

    // The argument list up to its closing parenthesis.
    private static string Call(string text, int start)
    {
        var depth = 1;
        for (var index = start; index < text.Length; index++)
        {
            depth += text[index] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
            {
                return text[start..index];
            }
        }
        return text[start..];
    }

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !path.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "Tools" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    private static string Relative(string path) => Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/');
}
