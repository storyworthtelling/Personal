using System.Text.RegularExpressions;

namespace Watch.Tests.Guard;

/// <summary>
/// WD §13: <c>WatchTokens</c> is the only file in <c>Watch/</c> with a colour or a type size, and no
/// watch project names a Paper &amp; Ink token or font (A33). Each finding is a file and a line.
/// </summary>
public class WatchLeakGuardTests
{
    private static readonly Regex Hex = new(@"#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\b", RegexOptions.Compiled);
    private static readonly Regex ColourCall = new(@"Color\.(?:FromArgb|ParseColor)\s*\(", RegexOptions.Compiled);
    private static readonly Regex TextSizeSet = new(@"\bTextSize\s*=\s*-?\d", RegexOptions.Compiled);
    private static readonly Regex TextSizeCall = new(@"\bSetTextSize\s*\([^)]*?\b\d+(?:\.\d+)?[fFdDmM]?\b", RegexOptions.Compiled);

    private static readonly string[] PaperAndInk =
    [
        "PaperColorTokens", "PaperUi", "PaperTheme", "Source Serif", "Source Sans", "Cascadia", "Italianno",
    ];

    [Fact]
    public void NoHexOutsideTokens()
    {
        var findings = Scan(line => Hex.IsMatch(line) || ColourCall.IsMatch(line));

        Assert.True(findings.Count == 0, Report("A colour outside WatchTokens", findings));
    }

    [Fact]
    public void NoTypeSizeOutsideTokens()
    {
        var findings = Scan(line => TextSizeSet.IsMatch(line) || TextSizeCall.IsMatch(line));

        Assert.True(findings.Count == 0, Report("A type size outside WatchTokens", findings));
    }

    [Fact]
    public void NoPaperAndInkInTheWatch()
    {
        var findings = Scan(line => PaperAndInk.Any(name => line.Contains(name, StringComparison.Ordinal)));

        Assert.True(findings.Count == 0, Report("A Paper & Ink name in the watch", findings));
    }

    [Fact]
    public void TheGuardSeesTheWatch()
    {
        Assert.Contains(WatchSources(), path => path.EndsWith("DayColour.cs", StringComparison.Ordinal));
    }

    private static List<string> Scan(Func<string, bool> offends)
    {
        var root = RepoRoot();
        var findings = new List<string>();
        foreach (var path in WatchSources())
        {
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                if (offends(lines[index]))
                {
                    findings.Add($"{Path.GetRelativePath(root, path)}:{index + 1}");
                }
            }
        }
        return findings;
    }

    private static string Report(string what, List<string> findings) =>
        $"{what}:{Environment.NewLine}{string.Join(Environment.NewLine, findings)}";

    private static IEnumerable<string> WatchSources()
    {
        var watch = Path.Combine(RepoRoot(), "Watch");
        var tests = Path.Combine(watch, "Watch.Tests") + Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(watch, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(tests, StringComparison.Ordinal))
            .Where(path => !IsBuildOutput(path))
            .Where(path => Path.GetFileName(path) != "WatchTokens.cs");
    }

    private static bool IsBuildOutput(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar);
        return parts.Contains("bin") || parts.Contains("obj");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException($"No Directory.Packages.props above {AppContext.BaseDirectory}.");
    }
}
