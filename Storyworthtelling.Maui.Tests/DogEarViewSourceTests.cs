namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// Finding 6 (mockup 17): the ear is 40dp, drawn from the mockup's own flap and a STROKED back-turn
/// arrow. It was 72dp, with the flap running to the foot of the fold and the arrow typed as the glyph
/// "↶" in Serif Semibold (a font choice standing in for a drawn mark).
///
/// <para>Read by content: <c>DogEar.cs</c> is a MAUI view and cannot be constructed in a plain xunit
/// run. The geometry itself is the pure <c>DogEarGeometry</c>, tested as real code.</para>
/// </summary>
public class DogEarViewSourceTests
{
    [Fact]
    public void TheEarIsTheMockupsFortyDp()
    {
        var source = Source();

        Assert.Contains("public const int SizeDp = DogEarGeometry.SizeDp;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBackTurnIsAStrokedPathNotAGlyphInAFont()
    {
        var source = Source();

        Assert.DoesNotContain("↶", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SerifSemibold", source, StringComparison.Ordinal);
        Assert.Contains("DogEarGeometry.ArrowArc", source, StringComparison.Ordinal);
        Assert.Contains("DogEarGeometry.ArrowHead", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFlapIsTheMockupsFlatPaperDeepWithAHairlineCrease()
    {
        var source = Source();

        Assert.Contains("DogEarGeometry.FlapTip", source, StringComparison.Ordinal);
        Assert.Contains("token(\"PaperDeep\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BothHuesWearTheirOwnTokenOnTheHole()
    {
        // The hole is filled through the palette, so the two new hues need no code here.
        Assert.Contains("DogEarPalette.TokenFor(fold.Hue)", Source(), StringComparison.Ordinal);
    }

    private static string Source() => File.ReadAllText(RepoFile("Storyworthtelling.Maui/Views/DogEar.cs"));

    private static string RepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
