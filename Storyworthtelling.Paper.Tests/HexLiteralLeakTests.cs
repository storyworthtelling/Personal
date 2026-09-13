using System.Text.RegularExpressions;

namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// Enforces the DESIGN.md §16 token-file registry for the shared Paper library: colour-hex literals may
/// appear only in the registered token file (<c>PaperColorTokens.cs</c>). Every other source resolves its
/// colours through the tokens or the <see cref="PaperPalette"/>.
/// </summary>
public class HexLiteralLeakTests
{
    private static readonly string[] AllowedSuffixes =
    {
        Path.Combine("Storyworthtelling.Paper", "PaperColorTokens.cs"),
        // The second registered token file (§16): the per-app IDENTITY hues a printed page signs
        // with, held to IconStudio's values by AppIdentityParityTests. Registered rather than
        // exempted — it is scanned for being a token file, not excused for being inconvenient.
        Path.Combine("Storyworthtelling.Paper", "AppIdentityTokens.cs"),
        Path.Combine("Storyworthtelling.Paper", "MaterialTokens.cs"),
    };

    [Fact]
    public void NoColorHexLiteralsOutsideTokenFile()
    {
        var root = PaperLibraryRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
                continue;
            if (AllowedSuffixes.Any(suffix => file.EndsWith(suffix, StringComparison.Ordinal)))
                continue;
            if (Regex.IsMatch(File.ReadAllText(file), "\"#[0-9A-Fa-f]{6,8}\""))
                offenders.Add(relative);
        }

        Assert.True(offenders.Count == 0,
            "Colour hex literals outside PaperColorTokens.cs (use the tokens / PaperPalette):\n"
            + string.Join("\n", offenders));
    }

    // Walks up from the test assembly to the repo root (marked by DESIGN.md), then to the Paper library.
    private static string PaperLibraryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DESIGN.md")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Storyworthtelling.Paper");
    }
}
