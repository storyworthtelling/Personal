using System.Text.RegularExpressions;
using Constitution.Tests.Guard;

namespace Constitution.Tests.Scraps;

/// <summary>
/// C-3.2.3 / D41 — Paper &amp; Ink has no shadows: an edge, a tint and the tilt are what separate a
/// scrap from the page. This reads the code and layout files under <c>Constitution/</c> for the
/// ways a view gets a shadow on Android (a shadow layer on a paint, a raised-surface setting on a
/// view or a card, a Z translation), so a painter cannot add one without a test saying so. There are no
/// scrap painters yet, so it must find none; it is kept honest by planted fixtures and by a floor
/// on the files it actually reads.
///
/// <para>Markdown is not scanned: the design notes name the rule in words. The names it looks for
/// are assembled at run time, so this file does not contain what it forbids.</para>
/// </summary>
public class NoShadowsTests
{
    private const int CodeFileFloor = 15;

    private static readonly string[] CodeExtensions = [".cs", ".axml", ".xml", ".xaml"];

    private static readonly Regex Forbidden = new(
        string.Join("|",
            "Set" + "Shadow" + "Layer",
            "Shadow" + "Layer",
            "Shadow" + "Radius",
            "Drop" + "Shadow",
            "Box" + "Shadow",
            "Eleva" + "tion",
            "translation" + "Z",
            "Translation" + "Z"),
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static IEnumerable<Finding> Find(string path, string text)
    {
        if (!CodeExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            yield break;
        }

        foreach (Match match in Forbidden.Matches(text))
        {
            yield return new Finding(GuardScan.LineOf(text, match.Index), "shadow");
        }
    }

    [Fact]
    public void NoScrapPainterDrawsAShadow()
    {
        var result = GuardScan.Run(ConstitutionSource.Root(), Find);

        Assert.True(result.Hits.Count == 0,
            "A shadow or raised-surface setting is under Constitution/; scraps are separated by edge, tint and tilt:\n"
            + string.Join("\n", result.Hits));
    }

    [Fact]
    public void TheScanReadsRealCodeFiles()
    {
        var result = GuardScan.Run(ConstitutionSource.Root(), Find);
        var code = result.Scanned.Where(f => CodeExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToList();

        Assert.True(code.Count >= CodeFileFloor, $"Only {code.Count} code files were scanned.");
        Assert.Contains("Constitution.Domain/Scraps/ScrapLayout.cs", code);
        Assert.Contains("Constitution.Tests/Scraps/NoShadowsTests.cs", code);
    }

    [Theory]
    [InlineData("Set" + "Shadow" + "Layer")]
    [InlineData("Shadow" + "Layer")]
    [InlineData("android:eleva" + "tion")]
    [InlineData("view." + "Eleva" + "tion = 4f;")]
    [InlineData("Card" + "Eleva" + "tion")]
    [InlineData("set" + "Eleva" + "tion")]
    [InlineData("android:translation" + "Z")]
    [InlineData("Drop" + "Shadow")]
    public void EachForbiddenNameIsCaughtInPlantedCode(string planted)
    {
        var root = GuardScan.TempRoot();
        try
        {
            GuardScan.Plant(root, "Painters/ScrapPainter.cs", "class P {\n  void Draw() { " + planted + "; }\n}\n");
            GuardScan.Plant(root, "Resources/layout/card.axml", "<Card " + planted + " />\n");
            GuardScan.Plant(root, "notes.md", "# notes\n" + planted + "\n");

            var result = GuardScan.Run(root.FullName, Find);

            Assert.Contains(result.Hits, h => h.File == "Painters/ScrapPainter.cs" && h.Line == 2);
            Assert.Contains(result.Hits, h => h.File == "Resources/layout/card.axml" && h.Line == 1);
            Assert.DoesNotContain(result.Hits, h => h.File == "notes.md");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void OrdinaryPaintCodeIsNotFlagged()
    {
        var root = GuardScan.TempRoot();
        try
        {
            GuardScan.Plant(root, "Painters/Clean.cs", "paint.Color = tint; canvas.DrawPath(edge, paint); canvas.Rotate(tilt);\n");

            Assert.Empty(GuardScan.Run(root.FullName, Find).Hits);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
