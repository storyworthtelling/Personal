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
        Path.Combine("Folio.Tests", "Design", "DesignTokensTests.cs"),

        // The one page that may not ask the design system for a colour, because its job is to be
        // readable when the design system is the thing that failed. It is shown only when the app
        // cannot build its first screen — on a phone, where the crash log sits in private storage
        // and an app that dies on open has no Settings page to export it from. A token lookup here
        // could throw for the same reason the app just did, and the owner would be back to a blank
        // stop with nothing to read. Guarded narrowly by OnlyTheStartupFailurePageMayHardCode below.
        Path.Combine("Folio.Android", "Pages", "StartupFailurePage.cs"),
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

    /// <summary>
    /// The startup-failure page's exemption is narrow and has to stay narrow: it is allowed its own
    /// colours <b>because</b> it asks the kit for nothing. The moment it reads a token it is no
    /// longer a page that survives the kit being unavailable, and the exemption above becomes a hole
    /// in the rule rather than a reason for one.
    /// </summary>
    [Fact]
    public void TheStartupFailurePageAsksTheDesignSystemForNothing()
    {
        var page = Path.Combine(
            DesignTestHelpers.FolioRoot(), "Folio.Android", "Pages", "StartupFailurePage.cs");
        var source = File.ReadAllText(page);

        Assert.DoesNotContain("PaperResources", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StaticResource", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DesignTokens", source, StringComparison.Ordinal);
    }
}
