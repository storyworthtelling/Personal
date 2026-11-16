using System.Text.RegularExpressions;

namespace Index.Tests.Design;

/// <summary>
/// Enforces the DESIGN.md token-file registry: no color hex literals in Ledger source outside the
/// sanctioned token files and generated icon resources.
///
/// <para>Release B (Guard G12) deleted both of the head-local exemptions this list used to carry —
/// <c>Ledger.Domain/Design/DesignTokens.cs</c> now holds only <c>Motion</c>'s millisecond constants,
/// and <c>Resources/Styles/DesignTokens.xaml</c> is gone entirely. The registered token file is now
/// the shared kit's <c>Storyworthtelling.Maui/Resources/Styles/Paper.xaml</c>, outside this head's
/// own tree — so there was nothing left in Ledger's own source allowed to hold a hex literal.</para>
///
/// <para>Release B's Part 1/2 reopened that list for <c>Ledger.Domain/Design/MaterialTokens.cs</c>, which holds
/// D122/D126/D127/D129/D131/D141's card/graph-paper/post-it hex — a registered token file in the DESIGN.md §16
/// sense, a place a colour is DECIDED. <c>LedgerAreas.cs</c> was on it too, holding the five area hues retyped;
/// it now names <c>AppIdentityTokens</c> and <c>PaperColorTokens</c> instead (audit W8.5) and holds no hex.</para>
/// </summary>
public class HexLiteralLeakTests
{
    private static readonly string[] AllowedSuffixes =
    [
        Path.Combine("Ledger.Domain", "Design", "MaterialTokens.cs"),
        // A single file, not a directory: PlaidClientTests' GetInstitution_ParsesPrimaryColor stubs a
        // Plaid wire response carrying a sample primary_color — Plaid's own hex, not a colour Index
        // is deciding, quoted in a fake HTTP body the same way every other fixture in that file quotes
        // a sample account id or merchant name.
        Path.Combine("Ledger.Tests", "External", "PlaidClientTests.cs"),
    ];

    private static readonly string[] AllowedDirectories =
    {
        Path.Combine("Ledger.Android", "Resources", "values"),
        Path.Combine("Ledger.Android", "Resources", "mipmap-anydpi-v26"),
        Path.Combine("Ledger.Android", "Resources", "drawable"),
        // Test fixtures, not app source: PostItTintTests/GraphPaperContrastTests assert D122/D127's
        // computed colours against known reference hexes (a bank's illustrative brand colour, the
        // identity ground, the paper background) the same way DesignTokensTests elsewhere in the suite
        // hardcodes sample hex for a contrast check. This guard's job is to keep the APP's rendering
        // decisions in one place; a test's own literal comparison values are not that decision.
        Path.Combine("Ledger.Tests", "Design"),
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
