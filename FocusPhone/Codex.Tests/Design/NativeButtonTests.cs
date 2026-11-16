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

        // A marker fill of the deep tier (§2, W8.8); the drawable shows the press as an inset Hairline (§8).
        Assert.Contains("new MarkerFillDrawable(", primary);
        Assert.Contains("PaperTheme.ActionDeep, PaperTheme.Ink, PaperTheme.Hairline", primary);
        Assert.DoesNotContain("PressableFill", source);
    }

    private static string PaperUi() =>
        File.ReadAllText(Path.Combine(DesignTestHelpers.FocusPhoneRoot(), "Codex.Ui.Android", "PaperUi.cs"));
}

/// <summary>
/// The native half of report 14 §1 and §4–5 (audit W8.3b): every pinned bar is one shape, and the binder
/// strip runs to the foot of the screen with the bar under the page only (owner's ruling).
/// </summary>
public class NativeFrameTests
{
    [Fact]
    public void TheBottomBarArrangesByBarOrderAndPadsFromTheBarTokens()
    {
        var bar = Method(Kit("PaperUi.cs"), "public static LinearLayout BottomBar(");

        Assert.Contains("BarOrder.Arrange(", bar);
        Assert.Contains("DesignTokens.Shape.BarPaddingX", bar);
        Assert.Contains("DesignTokens.Shape.BarPaddingY", bar);
        Assert.DoesNotContain("MarginEnd", bar);
    }

    [Fact]
    public void ThePrimaryButtonIsKnownToTheBar()
    {
        var primary = Method(Kit("PaperUi.cs"), "public static Button PrimaryButton(");
        Assert.Contains("Primaries.AddOrUpdate(button", primary);
    }

    /// <summary>The frame a secondary screen wears carries its bar in the page column, beside the strip.</summary>
    [Fact]
    public void BinderFrameSeatsTheBarInThePageColumn()
    {
        var frame = Kit("BinderFrame.cs");
        Assert.Contains("public static void SetBar(View spread, View? bar)", frame);
        Assert.Contains("page.AddView(barHost", frame);
    }

    /// <summary>No shell or secondary screen hangs its bar under the whole spread any more.</summary>
    [Theory]
    [InlineData("Score.Android/ScoreActivity.cs")]
    [InlineData("Post.Android/PostActivity.cs")]
    [InlineData("Plate.Android/PlateActivity.cs")]
    public void TheShellsBarSitsUnderThePage(string shell)
    {
        var source = File.ReadAllText(Path.Combine(DesignTestHelpers.FocusPhoneRoot(), shell));
        Assert.DoesNotContain("root.AddView(bottomBarSlot", source);
        Assert.Contains("page.AddView(bottomBarSlot", source);
    }

    [Fact]
    public void NoSecondaryScreenAddsItsBarBesideTheFrame()
    {
        var offenders = Directory.EnumerateFiles(DesignTestHelpers.FocusPhoneRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains("Tests"))
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("BinderFrame.Wrap", StringComparison.Ordinal)
                    && text.Contains("root.AddView(PaperUi.BottomBar(", StringComparison.Ordinal);
            })
            .ToList();

        Assert.True(offenders.Count == 0, "Hangs its bar under the strip: " + string.Join(", ", offenders));
    }

    private static string Kit(string file) =>
        File.ReadAllText(Path.Combine(DesignTestHelpers.FocusPhoneRoot(), "Codex.Ui.Android", file));

    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No {signature}");
        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            depth += source[i] == '{' ? 1 : source[i] == '}' ? -1 : 0;
            if (depth == 0)
            {
                return source[start..(i + 1)];
            }
        }

        return source[start..];
    }
}
