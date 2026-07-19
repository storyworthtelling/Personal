using System.Text.RegularExpressions;
using Tally.Domain.Design;

namespace Tally.Tests.Design;

public class DesignTokensTests
{
    internal static readonly IReadOnlyDictionary<string, string> Colors = new Dictionary<string, string>
    {
        ["Paper"] = DesignTokens.Paper,
        ["PaperBright"] = DesignTokens.PaperBright,
        ["PaperDeep"] = DesignTokens.PaperDeep,
        ["PaperShade"] = DesignTokens.PaperShade,
        ["Hairline"] = DesignTokens.Hairline,
        ["Ink"] = DesignTokens.Ink,
        ["InkSoft"] = DesignTokens.InkSoft,
        ["InkMuted"] = DesignTokens.InkMuted,
        ["Accent"] = DesignTokens.Accent,
        ["AccentDeep"] = DesignTokens.AccentDeep,
    };

    [Fact]
    public void AllColorsAreUppercaseSixDigitHex()
    {
        foreach (var (name, value) in Colors)
        {
            Assert.True(Regex.IsMatch(value, "^#[0-9A-F]{6}$"), $"{name} = {value} is not #RRGGBB uppercase hex");
        }
    }

    [Theory]
    [InlineData("Ink", "Paper", 7.0)]
    [InlineData("InkMuted", "Paper", 3.0)]
    [InlineData("Accent", "Paper", 3.0)]
    [InlineData("AccentDeep", "Paper", 4.5)]
    [InlineData("PaperBright", "Accent", 4.5)]
    [InlineData("Ink", "PaperDeep", 7.0)]
    public void ContrastMeetsDesignBibleThreshold(string fg, string bg, double minimum)
    {
        var ratio = DesignTestHelpers.ContrastRatio(Colors[fg], Colors[bg]);
        Assert.True(ratio >= minimum, $"{fg} on {bg} is {ratio:F2}:1, below {minimum}:1");
    }

    [Fact]
    public void TypeScaleDescends()
    {
        var scale = new[]
        {
            DesignTokens.Type.Display, DesignTokens.Type.Title, DesignTokens.Type.Heading,
            DesignTokens.Type.Body, DesignTokens.Type.Secondary, DesignTokens.Type.Label,
        };
        for (var i = 1; i < scale.Length; i++)
        {
            Assert.True(scale[i] < scale[i - 1], $"Type scale not strictly descending at index {i}");
        }
    }
}
