using System.Text.RegularExpressions;
using Constitution.Tests.Guard;

namespace Constitution.Tests.Scrapbook;

/// <summary>
/// C-3.2.2 / C-3.2.3: the scrapbook painters read every colour through <c>MaterialTokens</c> (or
/// <c>PaperTheme</c>) and every type size through <c>DesignTokens</c>. The hex scan elsewhere
/// catches a quoted colour; this catches the other ways a colour or size gets into a painter on
/// Android: a built colour (<c>Color.Argb</c>), a named one, a parse of anything but a registered
/// token, and a numeric text size. The names it looks for are assembled at run time so this file
/// does not hold what it forbids.
/// </summary>
public class PainterTokensTests
{
    private const string Folder = "Constitution.Android/Scrapbook/";
    private const int PainterFileFloor = 12;

    private static readonly string[] NamedColours =
        ["White", "Black", "Red", "Green", "Blue", "Gray", "Grey", "Yellow", "Cyan", "Magenta", "DarkGray", "LightGray", "Transparent"];

    private static readonly Regex BuiltColour = new(
        @"\bColor\s*\.\s*(?:" + "Ar" + "gb|" + "Rg" + "b|" + "Hs" + @"v)\s*\(" +
        @"|\bnew\s+Color\s*\(" +
        @"|\bColor\s*\.\s*(?:" + string.Join("|", NamedColours) + @")\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex ParsedColour = new(
        @"\bColor\s*\.\s*ParseColor\s*\(\s*(?!MaterialTokens\s*\.)",
        RegexOptions.CultureInvariant);

    private static readonly Regex NumericTextSize = new(
        @"\bTextSize\s*=\s*[\d.]|\bSetTextSize\s*\([^;]*,\s*[\d.]+f?\s*\)|\.\s*Sp\s*\(\s*\w+\s*,\s*[\d.]",
        RegexOptions.CultureInvariant);

    private static IEnumerable<Finding> Find(string path, string text)
    {
        if (!path.StartsWith(Folder, StringComparison.Ordinal) || !path.EndsWith(".cs", StringComparison.Ordinal))
        {
            yield break;
        }

        foreach (Match match in BuiltColour.Matches(text))
        {
            yield return new Finding(GuardScan.LineOf(text, match.Index), "built or named colour");
        }

        foreach (Match match in ParsedColour.Matches(text))
        {
            yield return new Finding(GuardScan.LineOf(text, match.Index), "colour parsed from something other than MaterialTokens");
        }

        foreach (Match match in NumericTextSize.Matches(text))
        {
            yield return new Finding(GuardScan.LineOf(text, match.Index), "numeric text size");
        }
    }

    [Fact]
    public void ThePaintersReadColoursAndSizesOnlyFromTokens()
    {
        var result = GuardScan.Run(ConstitutionSource.Root(), Find);

        Assert.True(result.Hits.Count == 0,
            "A scrapbook painter builds a colour or a size of its own; read it from MaterialTokens / PaperTheme / DesignTokens:\n"
            + string.Join("\n", result.Hits));
    }

    [Fact]
    public void TheScanReadsTheScrapbookFiles()
    {
        var result = GuardScan.Run(ConstitutionSource.Root(), Find);
        var read = result.Scanned.Count(f => f.StartsWith(Folder, StringComparison.Ordinal) && f.EndsWith(".cs", StringComparison.Ordinal));

        Assert.True(read >= PainterFileFloor, $"Only {read} scrapbook source files were scanned; the painters are not where the guard looks.");
    }

    [Theory]
    [InlineData("var c = Color." + "Ar" + "gb(20, 1, 2, 3);")]
    [InlineData("var c = Color." + "Rg" + "b(1, 2, 3);")]
    [InlineData("var c = new Color(1, 2, 3, 4);")]
    [InlineData("paint.Color = Color.White;")]
    [InlineData("paint.Color = Color.Transparent;")]
    [InlineData("var c = Color.ParseColor(someString);")]
    [InlineData("var c = Color.ParseColor(PaperTheme.Ink.ToString());")]
    [InlineData("paint.TextSize = 12f;")]
    [InlineData("view.SetTextSize(Android.Util.ComplexUnitType.Sp, 14f);")]
    [InlineData("paint.TextSize = PaperTheme.Sp(context, 11f);")]
    public void EachWayInIsCaughtInPlantedCode(string planted)
    {
        var root = GuardScan.TempRoot();
        try
        {
            GuardScan.Plant(root, Folder + "Planted.cs", "class P {\n  void Draw() { " + planted + " }\n}\n");

            var result = GuardScan.Run(root.FullName, Find);

            Assert.Contains(result.Hits, h => h.File == Folder + "Planted.cs" && h.Line == 2);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("paint.Color = Color.ParseColor(MaterialTokens.TapeHex);")]
    [InlineData("paint.Color = PaperTheme.Hairline;")]
    [InlineData("paint.TextSize = PaperTheme.Sp(context, DesignTokens.TypeSp.Label);")]
    [InlineData("view.SetTextSize(Android.Util.ComplexUnitType.Sp, DesignTokens.TypeSp.Body);")]
    [InlineData("paint.Alpha = (int)(255 * (double)MaterialTokens.TapeAlpha);")]
    public void TokenReadsAreNotFlagged(string clean)
    {
        var root = GuardScan.TempRoot();
        try
        {
            GuardScan.Plant(root, Folder + "Clean.cs", "class P {\n  void Draw() { " + clean + " }\n}\n");

            Assert.Empty(GuardScan.Run(root.FullName, Find).Hits);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void FilesOutsideTheScrapbookAreNotThisGuardsBusiness()
    {
        var root = GuardScan.TempRoot();
        try
        {
            GuardScan.Plant(root, "Constitution.Android/Other.cs", "class P { void D() { paint.TextSize = 12f; } }\n");

            Assert.Empty(GuardScan.Run(root.FullName, Find).Hits);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
