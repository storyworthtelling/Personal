using LogLoopReader;

namespace RepoGuards.Tests;

/// <summary>A finding placed in a file. <see cref="ToString"/> is the line a failing guard prints — never the matched text.</summary>
internal sealed record Hit(string File, int Line, string Kind)
{
    public override string ToString() => $"{File}:{Line}: {Kind}";
}

internal sealed record ScanResult(IReadOnlyList<string> Scanned, IReadOnlyList<Hit> Hits);

/// <summary>
/// The walk for the two "public repository" guards (<c>NoKeysTests</c>, <c>InventedDataTests</c>):
/// which files are read, and how a finding becomes a file-and-line. It takes the root as an argument so
/// a test can point it at a temp directory holding a deliberately bad fixture and watch the guard fail
/// on it. Exclusions are build output, git's own folder and the generated code graph; the guards' own
/// sources are scanned like everything else, which is why their bad samples are assembled at run time.
/// </summary>
internal static class GuardScan
{
    /// <summary>Source, config, fixtures, workflows, mockups and the markdown docs.</summary>
    public static readonly string[] Extensions =
    [
        ".cs", ".csproj", ".sln", ".props", ".targets", ".xml", ".xaml", ".axml", ".json", ".yml", ".yaml",
        ".config", ".sh", ".ps1", ".py", ".ini", ".toml", ".properties", ".env", ".txt", ".csv", ".html", ".md",
    ];

    private static readonly string[] SkippedFolders = ["bin", "obj", ".vs", ".git", "graphify-out", "node_modules"];

    public static IReadOnlyList<string> Files(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => !Path.GetRelativePath(root, file)
                .Split(Path.DirectorySeparatorChar)
                .Any(part => SkippedFolders.Contains(part)))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();

    /// <summary>Runs <paramref name="detect"/> (relative path, text) over every file under <paramref name="root"/>.</summary>
    public static ScanResult Run(string root, Func<string, string, IEnumerable<ShapeFinding>> detect)
    {
        var scanned = new List<string>();
        var hits = new List<Hit>();
        foreach (var file in Files(root))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            scanned.Add(relative);
            hits.AddRange(detect(relative, File.ReadAllText(file))
                .Distinct()
                .Select(finding => new Hit(relative, finding.Line, finding.Kind)));
        }

        return new ScanResult(scanned, hits);
    }

    /// <summary>The repository root: the nearest folder above the test binary holding both CLAUDE.md and DESIGN.md.</summary>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")) && File.Exists(Path.Combine(dir.FullName, "DESIGN.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root (CLAUDE.md beside DESIGN.md) above " + AppContext.BaseDirectory);
    }

    /// <summary>A temp directory for a planted-fixture test; the caller deletes it.</summary>
    public static DirectoryInfo TempRoot() => Directory.CreateTempSubdirectory("repo-guard-");

    public static void Plant(DirectoryInfo root, string relative, string content)
    {
        var path = Path.Combine(root.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
