using System.Text.RegularExpressions;
using FocusPhone.Domain.Design;
using Xunit;

namespace FocusPhone.Tests.Design;

public class DesignTokensTests
{
    private static readonly IReadOnlyDictionary<string, string> Colors = new Dictionary<string, string>
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

    [Fact]
    public void AllColorsAreDistinct()
    {
        Assert.Equal(Colors.Count, Colors.Values.Distinct().Count());
    }

    [Theory]
    [InlineData("Ink", "Paper", 7.0)]          // AAA body text
    [InlineData("InkSoft", "Paper", 4.5)]      // AA body text
    [InlineData("InkMuted", "Paper", 3.0)]     // large/secondary text
    [InlineData("Accent", "Paper", 3.0)]       // UI components, large text
    [InlineData("AccentDeep", "Paper", 4.5)]   // accent text at any size
    [InlineData("PaperBright", "Accent", 4.5)] // primary button text
    [InlineData("Ink", "PaperDeep", 7.0)]      // warning card body
    [InlineData("Ink", "PaperShade", 7.0)]     // pressed surfaces
    public void ContrastMeetsDesignBibleThreshold(string fg, string bg, double minimum)
    {
        var ratio = DesignTestHelpers.ContrastRatio(Colors[fg], Colors[bg]);
        Assert.True(ratio >= minimum, $"{fg} on {bg} is {ratio:F2}:1, below {minimum}:1");
    }

    [Fact]
    public void TypeScaleDescendsFromClockToLabel()
    {
        var scale = new[]
        {
            DesignTokens.TypeSp.Clock,
            DesignTokens.TypeSp.Countdown,
            DesignTokens.TypeSp.Display,
            DesignTokens.TypeSp.Title,
            DesignTokens.TypeSp.Heading,
            DesignTokens.TypeSp.Body,
            DesignTokens.TypeSp.Secondary,
            DesignTokens.TypeSp.Label,
        };
        for (var i = 1; i < scale.Length; i++)
        {
            Assert.True(scale[i] < scale[i - 1], $"Type scale not strictly descending at index {i}");
        }
    }

    [Fact]
    public void MonoMatchesSecondarySize()
    {
        Assert.Equal(DesignTokens.TypeSp.Secondary, DesignTokens.TypeSp.Mono);
    }

    [Fact]
    public void SpacingScaleIsMultiplesOfFourAndAscending()
    {
        var scale = new[]
        {
            DesignTokens.Space.X1, DesignTokens.Space.X2, DesignTokens.Space.X3,
            DesignTokens.Space.X4, DesignTokens.Space.X6, DesignTokens.Space.X8,
            DesignTokens.Space.X12, DesignTokens.Space.X16, DesignTokens.Space.X24,
        };
        for (var i = 0; i < scale.Length; i++)
        {
            Assert.True(scale[i] % 4 == 0, $"Spacing {scale[i]} is not a multiple of 4");
            if (i > 0)
            {
                Assert.True(scale[i] > scale[i - 1], "Spacing scale not ascending");
            }
        }
    }

    [Fact]
    public void FontAssetsUseTheSixCommittedFonts()
    {
        var root = DesignTestHelpers.FocusPhoneRoot();
        var fontsDir = Path.Combine(Directory.GetParent(root)!.FullName, "Fonts");
        var assets = new[]
        {
            DesignTokens.FontAssets.SerifRegular,
            DesignTokens.FontAssets.SerifSemibold,
            DesignTokens.FontAssets.SerifItalic,
            DesignTokens.FontAssets.SansRegular,
            DesignTokens.FontAssets.SansSemibold,
            DesignTokens.FontAssets.Mono,
        };
        Assert.Equal(assets.Length, assets.Distinct().Count());
        foreach (var asset in assets)
        {
            Assert.StartsWith("fonts/", asset);
            var file = Path.Combine(fontsDir, Path.GetFileName(asset));
            Assert.True(File.Exists(file), $"Font file missing from repo Fonts/: {file}");
        }
    }
}
