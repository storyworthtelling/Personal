using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Design;

/// <summary>
/// Enforces the DESIGN.md token-file registry: no color hex literals anywhere in
/// FocusPhone C# source outside the sanctioned token files. Restyling a screen means
/// consuming tokens, never introducing a local color.
/// </summary>
public class HexLiteralLeakTests
{
    private static readonly string[] AllowedSuffixes =
    {
        Path.Combine("Codex.Domain", "Design", "DesignTokens.cs"),
        Path.Combine("Codex.Tests", "Design", "DesignTokensTests.cs"),
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
            "Color hex literals outside token files (use Codex.Domain.Design.DesignTokens):\n" +
            string.Join("\n", offenders));
    }

    // ----- Constitution/ (C-3.2.1) -----
    //
    // Constitution has NO token file of its own: every material it draws is registered in the shared
    // Storyworthtelling.Paper/MaterialTokens.cs (DESIGN.md section 19), so nothing under Constitution/
    // may hold a quoted colour hex. The scan takes its root as an argument so a test can plant a
    // literal in a temp directory and watch it fail, rather than trusting a guard that has only ever
    // passed.

    [Fact]
    public void NoColorHexLiteralsInConstitution()
    {
        var root = Path.Combine(Path.GetDirectoryName(DesignTestHelpers.FocusPhoneRoot())!, "Constitution");
        Assert.True(Directory.Exists(root), "Constitution/ not found beside FocusPhone/");

        var offenders = HexLiteralScan.Offenders(root, "*.cs", "*.xaml");

        Assert.True(offenders.Count == 0,
            "Color hex literals under Constitution/ (register them in Storyworthtelling.Paper/MaterialTokens.cs):\n" +
            string.Join("\n", offenders));
    }

    [Fact]
    public void AColorLiteralPlantedInConstitutionSourceIsCaught()
    {
        var root = Directory.CreateTempSubdirectory("constitution-hex-");
        try
        {
            // Assembled at run time so this file does not itself hold the literal it plants.
            var planted = "var ground = \"" + "#" + "A1B2C3" + "\";";
            Directory.CreateDirectory(Path.Combine(root.FullName, "Constitution.Android", "Views"));
            File.WriteAllText(Path.Combine(root.FullName, "Constitution.Android", "Views", "Scrap.cs"), planted);
            Directory.CreateDirectory(Path.Combine(root.FullName, "Constitution.Android", "obj"));
            File.WriteAllText(Path.Combine(root.FullName, "Constitution.Android", "obj", "Generated.cs"), planted);

            var offenders = HexLiteralScan.Offenders(root.FullName, "*.cs", "*.xaml");

            var only = Assert.Single(offenders);
            Assert.Contains("Scrap.cs", only);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}

/// <summary>The shared walk behind the leak guards: which files hold a quoted colour hex.</summary>
internal static class HexLiteralScan
{
    public static IReadOnlyList<string> Offenders(string root, params string[] patterns)
    {
        var offenders = new List<string>();
        foreach (var pattern in patterns)
        {
            var literal = pattern == "*.cs" ? "\"#[0-9A-Fa-f]{6,8}\"" : "#[0-9A-Fa-f]{6,8}\\b";
            foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
                {
                    continue;
                }
                if (Regex.IsMatch(File.ReadAllText(file), literal))
                {
                    offenders.Add(relative);
                }
            }
        }
        return offenders;
    }
}

