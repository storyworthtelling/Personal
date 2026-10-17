using System.Text.RegularExpressions;
using Codex.Domain.Design;
using Xunit;

namespace Codex.Tests.Design;

/// <summary>
/// Codex.Ui.Android/Resources/values/colors.xml is a sanctioned second token
/// file (Android themes cannot read C# constants). This test keeps it byte-honest
/// with the Domain tokens so there is still only one source of truth.
/// </summary>
public class TokenParityTests
{
    [Fact]
    public void ColorsXmlMatchesDomainTokens()
    {
        var path = Path.Combine(
            DesignTestHelpers.FocusPhoneRoot(),
            "Codex.Ui.Android", "Resources", "values", "colors.xml");
        Assert.True(File.Exists(path), $"Missing {path}");

        var xml = File.ReadAllText(path);
        var parsed = Regex.Matches(xml, "<color name=\"(\\w+)\">(#[0-9A-Fa-f]{6})</color>")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.ToUpperInvariant());

        var expected = new Dictionary<string, string>
        {
            ["paper"] = DesignTokens.Paper,
            ["paperBright"] = DesignTokens.PaperBright,
            ["paperDeep"] = DesignTokens.PaperDeep,
            ["paperShade"] = DesignTokens.PaperShade,
            ["hairline"] = DesignTokens.Hairline,
            ["ink"] = DesignTokens.Ink,
            ["inkSoft"] = DesignTokens.InkSoft,
            ["inkMuted"] = DesignTokens.InkMuted,
            ["accent"] = DesignTokens.Accent,
            ["accentDeep"] = DesignTokens.AccentDeep,
        };

        Assert.Equal(expected.Count, parsed.Count);
        foreach (var (name, hex) in expected)
        {
            Assert.True(parsed.TryGetValue(name, out var actual), $"colors.xml missing '{name}'");
            Assert.Equal(hex, actual);
        }
    }

    // Constitution's ladder is written in four places (IconStudio paints the icon, Paper signs the
    // printed page, Domain feeds the shelf and screens, DESIGN.md §3 records it). Four copies of a
    // colour are four chances to drift, so one test reads all four and holds them to the same six.
    [Fact]
    public void ConstitutionLadderIsIdenticalInEveryFile()
    {
        var repo = Path.GetFullPath(Path.Combine(DesignTestHelpers.FocusPhoneRoot(), ".."));
        string[] tiers = ["Fill", "Ground", "Mark", "Text", "Deep", "Cloth"];

        var domain = new[]
        {
            DesignTokens.Identity.Constitution.Fill, DesignTokens.Identity.Constitution.Ground,
            DesignTokens.Identity.Constitution.Mark, DesignTokens.Identity.Constitution.Text,
            DesignTokens.Identity.Constitution.Deep, DesignTokens.Identity.Constitution.Cloth,
        };

        var iconStudio = File.ReadAllText(Path.Combine(repo, "Tools", "IconStudio", "Tokens.cs"));
        var paper = File.ReadAllText(Path.Combine(repo, "Storyworthtelling.Paper", "AppIdentityTokens.cs"));
        var design = File.ReadAllLines(Path.Combine(repo, "DESIGN.md"))
            .FirstOrDefault(line => line.StartsWith("| Constitution |", StringComparison.Ordinal));
        Assert.True(design is not null, "DESIGN.md §3's ladder table has no Constitution row.");
        var table = Regex.Matches(design!, "`(#[0-9A-Fa-f]{6})`").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(6, table.Length);

        for (var i = 0; i < tiers.Length; i++)
        {
            var name = $"Constitution{tiers[i]}Hex";
            // The Domain ladder is the reference; its values are pinned as literals in
            // DesignTokensTests.ConstitutionIdentityIsTheApprovedLadder (a registered token file).
            Assert.Equal(domain[i], ReadConst(iconStudio, name, "Tools/IconStudio/Tokens.cs"), ignoreCase: true);
            Assert.Equal(domain[i], ReadConst(paper, name, "Storyworthtelling.Paper/AppIdentityTokens.cs"), ignoreCase: true);
            Assert.Equal(domain[i], table[i], ignoreCase: true);
        }
    }

    private static string ReadConst(string source, string name, string file)
    {
        var match = Regex.Match(source, $@"{name}\s*=\s*""(#[0-9A-Fa-f]{{6}})""");
        Assert.True(match.Success, $"{file} declares no {name}.");
        return match.Groups[1].Value;
    }
}
