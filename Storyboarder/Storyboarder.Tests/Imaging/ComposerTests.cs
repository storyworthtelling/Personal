using Microsoft.Extensions.Options;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using Storyboarder.Domain.Imaging;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.Imaging;
using Storyboarder.Tests.Support;
using Xunit;

namespace Storyboarder.Tests.Imaging;

public class FileSystemImageStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "storyboarder-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private FileSystemImageStore Store() =>
        new(Options.Create(new StorageOptions { OutputRoot = root }));

    [Fact]
    public void Save_WritesUnderRoot_AndReturnsAbsolutePath()
    {
        var store = Store();
        var path = store.Save(Path.Combine("novel", "panel-1.png"), [1, 2, 3]);

        Assert.True(Path.IsPathRooted(path));
        Assert.StartsWith(Path.GetFullPath(root), path);
        Assert.True(store.Exists(path));
        Assert.Equal<byte[]>([1, 2, 3], store.Read(path));
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        var store = Store();
        store.Save("a.png", [1, 1]);
        var path = store.Save("a.png", [9, 9, 9]);

        Assert.Equal<byte[]>([9, 9, 9], store.Read(path));
    }
}

public class SkiaPageComposerTests
{
    private static SkiaPageComposer Composer() =>
        new(Options.Create(new PageLayoutOptions { Width = 1024, Height = 1536, Margin = 40, Gutter = 24 }));

    [Fact]
    public void Compose_ProducesPngOfConfiguredSize()
    {
        var panels = new List<PanelArt> { new(SampleImages.Png(), null, null) };

        var bytes = Composer().Compose(panels);

        using var decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);
        Assert.Equal(1024, decoded!.Width);
        Assert.Equal(1536, decoded.Height);
    }

    [Fact]
    public void Compose_WithDialogueAndCaption_AcrossFourPanels_Succeeds()
    {
        var panels = Enumerable.Range(1, 4)
            .Select(i => new PanelArt(SampleImages.Png(color: SKColors.CornflowerBlue),
                Dialogue: $"Line {i} of dialogue that should wrap across the bubble nicely.",
                Caption: $"Meanwhile, in location {i}..."))
            .ToList();

        var bytes = Composer().Compose(panels);

        Assert.NotEmpty(bytes);
        using var decoded = SKBitmap.Decode(bytes);
        Assert.Equal(1024, decoded!.Width);
    }

    [Fact]
    public void Compose_NoPanels_Throws()
    {
        Assert.Throws<ArgumentException>(() => Composer().Compose([]));
    }
}

public class PdfVolumeComposerTests
{
    [Fact]
    public void Compose_ProducesPdf_WithOnePagePerImage()
    {
        var pages = new List<byte[]> { SampleImages.Png(200, 300), SampleImages.Png(200, 300) };

        var pdfBytes = new PdfVolumeComposer().Compose(pages);

        Assert.True(pdfBytes.Length > 4);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdfBytes, 0, 4));

        using var stream = new MemoryStream(pdfBytes);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Equal(2, document.PageCount);
    }
}
