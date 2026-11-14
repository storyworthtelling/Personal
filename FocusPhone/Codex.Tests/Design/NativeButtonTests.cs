using Xunit;
using Codex.Domain.Design;

namespace Codex.Tests.Design;

/// <summary>
/// The native kit's buttons are the ones DESIGN.md §3, §7 and §8 draw (audit W8.1): a filled
/// primary is the deep tier, pressing changes no colour, and every button is padded 18×11.
/// </summary>
public class NativeButtonTests
{
    [Fact]
    public void TheButtonPaddingIsEighteenByEleven()
    {
        Assert.Equal(18, DesignTokens.Shape.ButtonPaddingX);
        Assert.Equal(11, DesignTokens.Shape.ButtonPaddingY);
    }

    [Fact]
    public void ButtonsArePaddedFromTheButtonTokens()
    {
        var source = PaperUi();
        var buttonBase = source[source.IndexOf("private static Button ButtonBase", StringComparison.Ordinal)..];
        buttonBase = buttonBase[..buttonBase.IndexOf("return button;", StringComparison.Ordinal)];

        Assert.Contains("DesignTokens.Shape.ButtonPaddingX", buttonBase);
        Assert.Contains("DesignTokens.Shape.ButtonPaddingY", buttonBase);
    }

    [Fact]
    public void ThePrimaryFillsWithTheDeepTierAndPressingShowsAHairline()
    {
        var source = PaperUi();
        var primary = source[source.IndexOf("public static Button PrimaryButton", StringComparison.Ordinal)..];
        primary = primary[..primary.IndexOf("return button;", StringComparison.Ordinal)];

        Assert.Contains("PressedHairline(context, PaperTheme.ActionDeep)", primary);
        Assert.DoesNotContain("PressableFill", source);
    }

    private static string PaperUi() =>
        File.ReadAllText(Path.Combine(DesignTestHelpers.FocusPhoneRoot(), "Codex.Ui.Android", "PaperUi.cs"));
}
