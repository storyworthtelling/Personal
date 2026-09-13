using System.Text.RegularExpressions;

namespace Folio.Tests.Design;

/// <summary>
/// Enforces the DESIGN.md token-file registry: no color hex literals in
/// Folio source outside the sanctioned token files and generated
/// icon resources.
/// </summary>
public class HexLiteralLeakTests
{
    private static readonly string[] AllowedSuffixes =
    {
        Path.Combine("Folio.Domain", "Design", "DesignTokens.cs"),
        Path.Combine("Folio.Android", "Resources", "Styles", "DesignTokens.xaml"),
        Path.Combine("Folio.Tests", "Design", "DesignTokensTests.cs"),
    };

    private static readonly string[] AllowedDirectories =
    {
        Path.Combine("Folio.Android", "Platforms", "Android", "Resources"),
    };

    [Fact]
    public void NoColorHexLiteralsOutsideTokenFiles()
    {
        var root = DesignTestHelpers.FolioRoot();
        var offenders = new List<string>();

        foreach (var pattern in new[] { "*.cs", "*.xaml" })
        {
            foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
                {
                    continue;
                }
                if (AllowedSuffixes.Any(suffix => file.EndsWith(suffix, StringComparison.Ordinal)) ||
                    AllowedDirectories.Any(dir => relative.StartsWith(dir, StringComparison.Ordinal)))
                {
                    continue;
                }
                var source = File.ReadAllText(file);
                if (Regex.IsMatch(source, pattern == "*.cs" ? "\"#[0-9A-Fa-f]{6,8}\"" : "#[0-9A-Fa-f]{6,8}\\b"))
                {
                    offenders.Add(relative);
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Color hex literals outside token files (use DesignTokens):\n" + string.Join("\n", offenders));
    }
}
