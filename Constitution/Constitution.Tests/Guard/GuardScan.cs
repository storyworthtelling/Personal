namespace Constitution.Tests.Guard;

/// <summary>One thing a detector found: where, and what kind. Never the matched text itself.</summary>
internal sealed record Finding(int Line, string Kind);

/// <summary>A finding placed in a file. <see cref="ToString"/> is the line a failing guard prints.</summary>
internal sealed record Hit(string File, int Line, string Kind)
{
    public override string ToString() => $"{File}:{Line}: {Kind}";
}

internal sealed record ScanResult(IReadOnlyList<string> Scanned, IReadOnlyList<Hit> Hits);

/// <summary>
/// The shared walk for the two "public repository" guards (<c>NoKeysTests</c>,
/// <c>InventedDataTests</c>): which files are read, and how a finding becomes a file-and-line.
/// It takes the root as an argument — rather than always using <c>ConstitutionSource.Root()</c> —
/// so a test can point it at a temp directory holding a deliberately bad fixture and watch the
/// guard fail on it. The only exclusions are build output (<c>bin</c>, <c>obj</c>, <c>.vs</c>):
/// the guards' own sources are scanned like everything else, which is why their bad samples are
/// assembled at run time.
/// </summary>
internal static class GuardScan
{
    /// <summary>Source, config, fixtures and the markdown docs (plan, notes, spikes, progress).</summary>
    public static readonly string[] Extensions =
    [
        ".cs", ".csproj", ".sln", ".props", ".targets", ".xml", ".xaml", ".axml", ".json", ".yml", ".yaml",
        ".config", ".sh", ".ps1", ".ini", ".toml", ".properties", ".env", ".txt", ".csv", ".html", ".md",
    ];

    public static IReadOnlyList<string> Files(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => !Path.GetRelativePath(root, file)
                .Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj" or ".vs"))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();

    /// <summary>Runs <paramref name="detect"/> (relative path, text) over every file under <paramref name="root"/>.</summary>
    public static ScanResult Run(string root, Func<string, string, IEnumerable<Finding>> detect)
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

    /// <summary>1-based line number of <paramref name="index"/> in <paramref name="text"/>.</summary>
    public static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>A temp directory for a planted-fixture test; the caller deletes it.</summary>
    public static DirectoryInfo TempRoot() => Directory.CreateTempSubdirectory("constitution-guard-");

    public static void Plant(DirectoryInfo root, string relative, string content)
    {
        var path = Path.Combine(root.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
