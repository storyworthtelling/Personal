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
}
