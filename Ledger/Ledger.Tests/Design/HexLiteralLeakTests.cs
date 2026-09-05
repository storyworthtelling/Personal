using System.Text.RegularExpressions;

namespace Ledger.Tests.Design;

/// <summary>
/// Enforces the DESIGN.md token-file registry: no color hex literals in Ledger source outside the
/// sanctioned token files and generated icon resources.
/// </summary>
public class HexLiteralLeakTests
{
    private static readonly string[] AllowedSuffixes =
    {
        Path.Combine("Ledger.Domain", "Design", "DesignTokens.cs"),
        Path.Combine("Ledger.Android", "Resources", "Styles", "DesignTokens.xaml"),
        Path.Combine("Ledger.Tests", "Design", "DesignTokensTests.cs"),
    };

    private static readonly string[] AllowedDirectories =
    {
        Path.Combine("Ledger.Android", "Resources", "values"),
        Path.Combine("Ledger.Android", "Resources", "mipmap-anydpi-v26"),
        Path.Combine("Ledger.Android", "Resources", "drawable"),
    };

    [Fact]
    public void NoColorHexLiteralsOutsideTokenFiles()
    {
        var root = DesignTestHelpers.LedgerRoot();
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
