using IconStudio;
using SkiaSharp;

namespace IconStudio.Tests;

public class BrandAssetsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "iconstudio-brand-tests", Guid.NewGuid().ToString("N"));

    public BrandAssetsTests()
    {
        // BrandAssets needs the serif font under <root>/Fonts for the placeholder.
        var repoFonts = Path.Combine(RepoRoot(), "Fonts");
        var fonts = Path.Combine(root, "Fonts");
        Directory.CreateDirectory(fonts);
        File.Copy(Path.Combine(repoFonts, "SourceSerif4-Semibold.ttf"), Path.Combine(fonts, "SourceSerif4-Semibold.ttf"));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tools", "IconStudio")))
        {
            dir = dir.Parent;
        }
        return dir!.FullName;
    }

    [Fact]
    public void GrainTile_IsDeterministic_AndSubtle()
    {
        var first = GrainTile.RenderPng();
        var second = GrainTile.RenderPng();
        Assert.Equal(first, second);

        using var bitmap = SKBitmap.Decode(first);
        Assert.Equal(GrainTile.TileSize, bitmap.Width);
        var grainPixels = 0;
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                var alpha = bitmap.GetPixel(x, y).Alpha;
                Assert.True(alpha == 0 || alpha == GrainTile.Alpha, $"unexpected alpha {alpha}");
                if (alpha > 0)
                {
                    grainPixels++;
                }
            }
        }
        var fraction = grainPixels / (double)(bitmap.Width * bitmap.Height);
        Assert.InRange(fraction, 0.10, 0.25); // ~1 in 6 pixels
    }

    [Fact]
    public void Write_CreatesPlaceholderSource_AndAllConsumerCopies()
    {
        var written = BrandAssets.Write(root);

        Assert.True(File.Exists(Path.Combine(root, "Branding", "owner.png")));
        Assert.True(File.Exists(Path.Combine(root, "Storyboarder", "Storyboarder.Infrastructure", "Resources", "owner.png")));
        Assert.True(File.Exists(Path.Combine(root, "FocusPhone", "FocusPhone.Ui.Android", "Resources", "drawable-nodpi", "paper_grain.png")));
        Assert.True(written.Count >= 7);

        // The medallion is circle-masked: corners transparent, center opaque, ring present.
        var medallionPath = Path.Combine(root, "Storyboarder", "Storyboarder.Infrastructure", "Resources", "owner.png");
        using var medallion = SKBitmap.Decode(medallionPath);
        Assert.Equal(0, medallion.GetPixel(3, 3).Alpha);
        Assert.Equal(255, medallion.GetPixel(medallion.Width / 2, medallion.Height / 2).Alpha);
    }

    [Fact]
    public void Write_NeverClobbersAnExistingOwnerImage()
    {
        var ownerPath = Path.Combine(root, "Branding", "owner.png");
        Directory.CreateDirectory(Path.GetDirectoryName(ownerPath)!);
        // A recognizable stand-in for the owner's real photo: a solid accent square.
        using (var surface = SKSurface.Create(new SKImageInfo(64, 64)))
        {
            surface.Canvas.Clear(Tokens.Accent);
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(ownerPath, data.ToArray());
        }
        var original = File.ReadAllBytes(ownerPath);

        BrandAssets.Write(root);

        Assert.Equal(original, File.ReadAllBytes(ownerPath));
        // And the consumer medallion derives from the photo (accent center).
        using var medallion = SKBitmap.Decode(Path.Combine(
            root, "Storyboarder", "Storyboarder.Infrastructure", "Resources", "owner.png"));
        Assert.Equal(Tokens.Accent, medallion.GetPixel(medallion.Width / 2, medallion.Height / 2));
    }
}
