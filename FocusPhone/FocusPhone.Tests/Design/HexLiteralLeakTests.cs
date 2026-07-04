using System.Text.RegularExpressions;
using Xunit;

namespace FocusPhone.Tests.Design;

/// <summary>
/// Enforces the DESIGN.md token-file registry: no color hex literals anywhere in
/// FocusPhone C# source outside the sanctioned token files. Restyling a screen means
/// consuming tokens, never introducing a local color.
/// </summary>
public class HexLiteralLeakTests
{
    private static readonly string[] AllowedSuffixes =
    {
        Path.Combine("FocusPhone.Domain", "Design", "DesignTokens.cs"),
        Path.Combine("FocusPhone.Tests", "Design", "DesignTokensTests.cs"),
    };

    [Fact]
    public void NoColorHexLiteralsOutsideTokenFiles()
    {
        var root = DesignTestHelpers.FocusPhoneRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
            {
                continue;
            }
            if (AllowedSuffixes.Any(suffix => file.EndsWith(suffix, StringComparison.Ordinal)))
            {
                continue;
            }
            var source = File.ReadAllText(file);
            if (Regex.IsMatch(source, "\"#[0-9A-Fa-f]{6,8}\""))
            {
                offenders.Add(relative);
            }
        }

        Assert.True(offenders.Count == 0,
            "Color hex literals outside token files (use FocusPhone.Domain.Design.DesignTokens):\n" +
            string.Join("\n", offenders));
    }
}
