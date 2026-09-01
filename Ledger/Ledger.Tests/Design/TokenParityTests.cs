using System.Text.RegularExpressions;

namespace Ledger.Tests.Design;

/// <summary>
/// The XAML dictionary is a sanctioned second token file (XAML styles cannot read C# constants). This
/// test keeps its colors and type sizes byte-honest with the Domain tokens so there is still only one
/// source of truth.
/// </summary>
public class TokenParityTests
{
    [Fact]
    public void DesignTokensXamlMatchesDomainColors()
    {
        var xaml = ReadTokensXaml();
        var parsed = Regex.Matches(xaml, "<Color x:Key=\"(\\w+)\">(#[0-9A-Fa-f]{6})</Color>")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.ToUpperInvariant());

        Assert.Equal(DesignTokensTests.Colors.Count, parsed.Count);
        foreach (var (name, hex) in DesignTokensTests.Colors)
        {
            Assert.True(parsed.TryGetValue(name, out var actual), $"DesignTokens.xaml missing color '{name}'");
            Assert.Equal(hex, actual);
        }
    }

    [Fact]
    public void DesignTokensXamlMatchesDomainTypeScale()
    {
        var xaml = ReadTokensXaml();
        var parsed = Regex.Matches(xaml, "<x:Double x:Key=\"(\\w+)Size\">(\\d+)</x:Double>")
            .ToDictionary(m => m.Groups[1].Value, m => double.Parse(m.Groups[2].Value));

        var expected = new Dictionary<string, double>
        {
            ["Display"] = Ledger.Domain.Design.DesignTokens.Type.Display,
            ["Title"] = Ledger.Domain.Design.DesignTokens.Type.Title,
            ["Heading"] = Ledger.Domain.Design.DesignTokens.Type.Heading,
            ["Body"] = Ledger.Domain.Design.DesignTokens.Type.Body,
            ["Secondary"] = Ledger.Domain.Design.DesignTokens.Type.Secondary,
            ["Label"] = Ledger.Domain.Design.DesignTokens.Type.Label,
            ["Mono"] = Ledger.Domain.Design.DesignTokens.Type.Mono,
        };
        Assert.Equal(expected.Count, parsed.Count);
        foreach (var (name, size) in expected)
        {
            Assert.True(parsed.TryGetValue(name, out var actual), $"DesignTokens.xaml missing size '{name}Size'");
            Assert.Equal(size, actual);
        }
    }

    [Fact]
    public void ButtonStylesUseTheSoftStationeryRadius()
    {
        // DESIGN.md §4: radii 10–16; buttons/cards sit at 12.
        var xaml = ReadTokensXaml();
        var radii = Regex
            .Matches(xaml, "<Setter Property=\"CornerRadius\" Value=\"(\\d+)\" />")
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToList();
        Assert.NotEmpty(radii);
        Assert.All(radii, r => Assert.Equal(12, r));
        Assert.DoesNotContain("RoundRectangle 6", xaml);
        Assert.DoesNotContain("RoundRectangle 8", xaml);
    }

    private static string ReadTokensXaml()
    {
        var path = Path.Combine(
            DesignTestHelpers.LedgerRoot(),
            "Ledger.Android", "Resources", "Styles", "DesignTokens.xaml");
        Assert.True(File.Exists(path), $"Missing {path}");
        return File.ReadAllText(path);
    }
}
