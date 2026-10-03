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
/// <para>Release B's Part 1/2 (five tabs with their own hues, three new materials) reopened that list
/// by exactly two files, the same way <c>Tools/IconStudio/Tokens.cs</c> is the suite's registered home
/// for an identity ladder: <c>LedgerAreas.cs</c> holds D115/D117/D120's five area hues (retyped from
/// IconStudio's committed values rather than referenced, per that file's own remarks — Ledger.Android
/// takes no project reference on Tools/IconStudio), and <c>Ledger.Domain/Design/MaterialTokens.cs</c>
/// holds D122/D126/D127/D129/D131/D141's card/graph-paper/post-it hex. Both are registered token
/// files in the DESIGN.md §16 sense — a place a colour is DECIDED, not a screen that merely uses one
/// — which is the same bar every other entry on this allow-list has to clear.</para>
/// </summary>
public class HexLiteralLeakTests
{
    private static readonly string[] AllowedSuffixes =
    [
        Path.Combine("Ledger.Android", "Pages", "LedgerAreas.cs"),
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
