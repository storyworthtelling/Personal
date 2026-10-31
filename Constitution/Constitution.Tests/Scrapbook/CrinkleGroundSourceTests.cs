using System.Text.RegularExpressions;
using Constitution.Tests.Guard;

namespace Constitution.Tests.Scrapbook;

/// <summary>
/// C-3.2.2 / D43: the ground is marks on paper and nothing lettered onto it, and it is the well's
/// ground, never the binder gutter's. The Android view cannot be loaded here, so these read its
/// source: it draws no text, and it sizes its field with <c>ForWell</c> (the well's own width)
/// rather than <c>ForPage</c>, which would subtract the gutter from a width that already excludes it.
/// </summary>
public class CrinkleGroundSourceTests
{
    private const string ViewFile = "Constitution.Android/Scrapbook/CrinkleGroundView.cs";

    private static readonly Regex Lettering = new(
        @"\b(?:DrawText\w*|DrawTextOnPath|StaticLayout|TextView|SetTextSize|Typeface|PaperUi)\b",
        RegexOptions.CultureInvariant);

    private static IEnumerable<Finding> FindLettering(string path, string text)
    {
        if (!path.EndsWith("CrinkleGroundView.cs", StringComparison.Ordinal))
        {
            yield break;
        }

        // Comments may name what the view does not do.
        var code = Regex.Replace(text, @"//[^\n]*", string.Empty);
        foreach (Match match in Lettering.Matches(code))
        {
            yield return new Finding(GuardScan.LineOf(code, match.Index), match.Value);
        }
    }

    [Fact]
    public void TheGroundViewDrawsNoText()
    {
        var result = GuardScan.Run(ConstitutionSource.Root(), FindLettering);

        Assert.Contains(ViewFile, result.Scanned);
        Assert.True(result.Hits.Count == 0, "Nothing is lettered onto the ground (D43):\n" + string.Join("\n", result.Hits));
    }

    [Theory]
    [InlineData("canvas.DrawText(\"Today\", 0, 0, paint);")]
    [InlineData("var view = new TextView(context);")]
    [InlineData("paint.Typeface = face;")]
    [InlineData("var layout = new StaticLayout(a, b);")]
    public void LetteringIsCaughtInPlantedCode(string planted)
    {
        var root = GuardScan.TempRoot();
        try
        {
            GuardScan.Plant(root, ViewFile, "class V {\n  void D() { " + planted + " }\n}\n");

            Assert.Contains(GuardScan.Run(root.FullName, FindLettering).Hits, h => h.Line == 2);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheFieldIsSizedFromTheWellNotThePage()
    {
        var text = File.ReadAllText(Path.Combine(ConstitutionSource.Root(), ViewFile));

        Assert.Contains("CrinkleField.ForWell(", text);
        Assert.DoesNotContain("ForPage(", text);
        Assert.DoesNotContain("Generate(", text);
    }

    [Fact]
    public void ThePerforationIsDrawnFromTheFieldsOwnFraction()
    {
        var text = File.ReadAllText(Path.Combine(ConstitutionSource.Root(), ViewFile));

        Assert.Contains("Perforation.YFraction", text);
    }
}
