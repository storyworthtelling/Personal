using Watch.Domain.Design;
using Watch.Domain.Kit;

namespace FaceStudio.Tests;

/// <summary>
/// Dash's launcher icon (P14 B4; WD §3, family A; glyph studies: "the glyph sits on a black disc like a Wear OS icon"):
/// the dash in Dash's glass on a round black icon, drawn from the kit's own glyph geometry, so the icon and the app's
/// glyph cannot drift apart (WD §13's parity rule). Generated and committed like the face; never hand-edited.
/// </summary>
public class DashIconTests
{
    private static string Root => FaceRead.RepoRoot();

    [Fact]
    public void TheCommittedIconsAreCurrent()
    {
        var folder = Path.Combine(Root, DashIcon.Folder);
        var generated = DashIcon.All();

        foreach (var file in generated)
        {
            var path = Path.Combine(folder, file.Path);
            Assert.True(File.Exists(path), $"{file.Path} is not committed; run FaceStudio.");
            Assert.True(FaceFiles.Same(file.Path, file.Bytes, File.ReadAllBytes(path)), $"{file.Path} differs from what FaceStudio makes.");
        }

        var committed = Directory.EnumerateFiles(folder, "ic_launcher*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
            .ToList();
        Assert.Equal(generated.Select(file => file.Path).Order(), committed.Order());
    }

    [Theory]
    [InlineData("mdpi", 48)]
    [InlineData("hdpi", 72)]
    [InlineData("xhdpi", 96)]
    [InlineData("xxhdpi", 144)]
    [InlineData("xxxhdpi", 192)]
    public void EveryDensityIsALauncherIcon(string density, int side)
    {
        var file = Assert.Single(DashIcon.All(), file => file.Path == $"mipmap-{density}/ic_launcher.png");
        var picture = PngPixels.Read(file.Bytes);

        Assert.Equal((side, side), (picture.Width, picture.Height));
    }

    [Fact]
    public void TheIconIsRoundOnBlack()
    {
        var icon = DashIcon.Draw(192);

        // Outside the disc: nothing. Inside it, away from the dash: true black, opaque.
        Assert.Equal(0, icon.AlphaAt(0, 0));
        Assert.Equal(0, icon.AlphaAt(191, 191));
        var ground = icon.ArgbAt(96, 30);
        Assert.Equal(Srgb.Argb(WatchTokens.Ground), ground);
    }

    [Fact]
    public void TheDashIsDashsGlassAtTheMockupsHour()
    {
        var icon = DashIcon.Draw(192);
        var glass = Srgb.Argb(DashIcon.Glass());

        AssertNear(glass, icon.ArgbAt(96, 96));
        Assert.Equal(new TimeOnly(16, 20), DashIcon.DrawnAt);
    }

    [Fact]
    public void TheDashIsTheKitsGlyph()
    {
        var side = 192;
        var icon = DashIcon.Draw(side);
        var glass = Srgb.Argb(DashIcon.Glass());
        var middle = side / 2;

        // Lit just inside each end of the dash, along its centre line.
        AssertNear(glass, icon.ArgbAt((int)(side * (GlyphGeometry.DashFrom + 0.02)), middle));
        AssertNear(glass, icon.ArgbAt((int)(side * (GlyphGeometry.DashTo - 0.02)), middle));

        // Dark again past the round end and its glow.
        var beyond = (int)(side * (GlyphGeometry.DashFrom - GlyphGeometry.DashStroke / 2 - GlyphGeometry.Glow - 0.02));
        Assert.Equal(Srgb.Argb(WatchTokens.Ground), icon.ArgbAt(beyond, middle));
    }

    private static void AssertNear(int expected, int actual)
    {
        for (var shift = 0; shift <= 24; shift += 8)
        {
            var want = (expected >> shift) & 0xFF;
            var got = (actual >> shift) & 0xFF;
            Assert.True(Math.Abs(want - got) <= 2, $"Expected {expected:X8}, got {actual:X8}.");
        }
    }
}
