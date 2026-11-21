namespace FaceStudio.Tests;

/// <summary>
/// A regenerated picture is the committed one when every pixel is within one step a channel: the preview's colours go
/// through the platform's pow and cbrt, whose last bit differs between machines (the first main run, Nov 2026).
/// </summary>
public class SamePictureTests
{
    [Fact]
    public void IdenticalBytesAreTheSame()
    {
        var png = Square(0xFF336699).Png();

        Assert.True(FaceFiles.Same("res/drawable-nodpi/preview.png", png, png));
    }

    [Fact]
    public void OneStepAChannelIsTheSame()
    {
        var committed = Square(0xFF336699).Png();
        var regenerated = Square(0xFF34659A).Png();

        Assert.False(committed.AsSpan().SequenceEqual(regenerated));
        Assert.True(FaceFiles.Same("res/drawable-nodpi/preview.png", regenerated, committed));
    }

    [Fact]
    public void TwoStepsAreADifferentPicture()
    {
        var committed = Square(0xFF336699).Png();
        var regenerated = Square(0xFF356699).Png();

        Assert.False(FaceFiles.Same("res/drawable-nodpi/preview.png", regenerated, committed));
    }

    [Fact]
    public void ADifferentSizeIsADifferentPicture()
    {
        var committed = Square(0xFF336699).Png();
        var regenerated = new Raster(3, 2).Png();

        Assert.False(FaceFiles.Same("res/drawable-nodpi/preview.png", regenerated, committed));
    }

    [Fact]
    public void TextIsComparedByItsBytes()
    {
        Assert.False(FaceFiles.Same("res/values/strings.xml", [0x61], [0x62]));
        Assert.True(FaceFiles.Same("res/values/strings.xml", [0x61], [0x61]));
    }

    [Fact]
    public void WritingKeepsACommittedPictureThatIsTheSame()
    {
        var folder = Directory.CreateTempSubdirectory("facestudio-same").FullName;
        var root = FaceRead.RepoRoot();
        FaceFiles.Write(root, folder);
        var preview = Path.Combine(folder, "res", "drawable-nodpi", "preview.png");
        var nudged = Nudge(File.ReadAllBytes(preview));
        File.WriteAllBytes(preview, nudged);

        FaceFiles.Write(root, folder);

        Assert.Equal(nudged, File.ReadAllBytes(preview));
        Directory.Delete(folder, recursive: true);
    }

    private static Raster Square(uint argb)
    {
        var raster = new Raster(2, 2);
        raster.Paint((x, y) => true, unchecked((int)argb));
        return raster;
    }

    /// <summary>The same picture with its first opaque pixel's red one step off, as another machine might draw it.</summary>
    private static byte[] Nudge(byte[] png)
    {
        var picture = PngPixels.Read(png);
        var rgba = picture.Rgba.ToArray();
        for (var at = 0; at < rgba.Length; at += 4)
        {
            if (rgba[at + 3] == 0xFF && rgba[at] < 0xFF)
            {
                rgba[at]++;
                return PngPixels.Encode(picture.Width, picture.Height, rgba);
            }
        }
        throw new InvalidOperationException("The preview has no opaque pixel to nudge.");
    }
}
