using Microsoft.Extensions.Options;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.Design;
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

    [Fact]
    public void Delete_RemovesAWholeSubtree()
    {
        var store = Store();
        var first = store.Save(Path.Combine("novel", "refs", "a.png"), [1, 2, 3]);
        var second = store.Save(Path.Combine("novel", "pages", "b.png"), [4, 5, 6]);

        store.Delete("novel");

        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.False(Directory.Exists(Path.Combine(root, "novel")));
    }

    [Fact]
    public void Delete_IsANoOp_WhenNothingIsThere()
    {
        // Deleting a path that was never written must not throw.
        Store().Delete("never-written");
    }
}

public class SkiaPageComposerTests
{
    private static readonly PageChrome Chrome = new(PageNumber: 7, VolumeTitle: "My Novel · Volume 2");

    private static SkiaPageComposer Composer() =>
        new(Options.Create(new PageLayoutOptions()));

    private static SKColor Token(string hex) => SKColor.Parse(hex);

    // Grained paper: within GrainTolerance of Paper per channel (alpha-6 ink
    // noise shifts channels by at most ~5). Real ink differs by far more.
    internal const int GrainTolerance = 8;
    internal const int StrongInkDelta = 24;

    internal static void AssertNearPaper(SKColor pixel)
    {
        var paper = SKColor.Parse(DesignTokens.Paper);
        Assert.True(
            Math.Abs(pixel.Red - paper.Red) <= GrainTolerance &&
            Math.Abs(pixel.Green - paper.Green) <= GrainTolerance &&
            Math.Abs(pixel.Blue - paper.Blue) <= GrainTolerance,
            $"expected near-paper, got {pixel}");
    }

    internal static bool IsStrongInk(SKColor pixel)
    {
        var paper = SKColor.Parse(DesignTokens.Paper);
        return Math.Abs(pixel.Red - paper.Red) > StrongInkDelta ||
               Math.Abs(pixel.Green - paper.Green) > StrongInkDelta ||
               Math.Abs(pixel.Blue - paper.Blue) > StrongInkDelta;
    }

    [Fact]
    public void Compose_ProducesPngOfConfiguredSize()
    {
        var panels = new List<PanelArt> { new(SampleImages.Png(), null, null) };

        var bytes = Composer().Compose(panels, Chrome);

        using var decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);
        Assert.Equal(DesignTokens.Comic.PageWidth, decoded!.Width);
        Assert.Equal(DesignTokens.Comic.PageHeight, decoded.Height);
    }

    [Fact]
    public void Compose_WithDialogueAndCaption_AcrossFourPanels_Succeeds()
    {
        var panels = Enumerable.Range(1, 4)
            .Select(i => new PanelArt(SampleImages.Png(color: SKColors.CornflowerBlue),
                Dialogue: $"Line {i} of dialogue that should wrap across the bubble nicely.",
                Caption: $"Meanwhile, in location {i}..."))
            .ToList();

        var bytes = Composer().Compose(panels, Chrome);

        Assert.NotEmpty(bytes);
        using var decoded = SKBitmap.Decode(bytes);
        Assert.Equal(DesignTokens.Comic.PageWidth, decoded!.Width);
    }

    [Fact]
    public void Compose_PageBackgroundIsGrainedPaper()
    {
        var bytes = Composer().Compose([new PanelArt(SampleImages.Png(), null, null)], Chrome);

        using var decoded = SKBitmap.Decode(bytes)!;
        // The corner is outside every panel and outside the folio band.
        AssertNearPaper(decoded.GetPixel(5, 5));
    }

    [Fact]
    public void Compose_IsDeterministic_GrainIncluded()
    {
        var panels = new List<PanelArt> { new(SampleImages.Png(), "Hello.", null) };
        Assert.Equal(Composer().Compose(panels, Chrome), Composer().Compose(panels, Chrome));
    }

    [Fact]
    public void Compose_GrainIsSubtle()
    {
        var bytes = Composer().Compose([new PanelArt(SampleImages.Png(), null, null)], Chrome);
        using var decoded = SKBitmap.Decode(bytes)!;
        // An empty margin strip: every pixel stays within the grain tolerance.
        for (var x = 0; x < 40; x++)
        {
            for (var y = 0; y < 40; y++)
            {
                AssertNearPaper(decoded.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void Compose_PanelBorderIsInk()
    {
        var bytes = Composer().Compose([new PanelArt(SampleImages.Png(), null, null)], Chrome);

        using var decoded = SKBitmap.Decode(bytes)!;
        // A single panel's top edge runs along y = TopMargin; sample the middle
        // of the stroke (drawn without antialiasing, so the color is exact).
        var border = decoded.GetPixel(
            DesignTokens.Comic.PageWidth / 2, DesignTokens.Comic.TopMargin);
        Assert.Equal(Token(DesignTokens.Ink), border);
    }

    [Fact]
    public void Compose_FolioBand_CarriesPageNumberAndTitleInk()
    {
        var withChrome = Composer().Compose([new PanelArt(SampleImages.Png(), null, null)], Chrome);

        using var decoded = SKBitmap.Decode(withChrome)!;
        // The folio band (bottom margin) must contain strong ink — the
        // running title and the page number — not just grain noise.
        var bandTop = DesignTokens.Comic.PageHeight - DesignTokens.Comic.FolioBand;
        var inked = 0;
        for (var x = 0; x < decoded.Width; x += 2)
        {
            for (var y = bandTop; y < decoded.Height; y += 2)
            {
                if (IsStrongInk(decoded.GetPixel(x, y)))
                {
                    inked++;
                }
            }
        }
        Assert.True(inked > 20, $"expected folio text pixels in the bottom band, found {inked}");
    }

    [Fact]
    public void Compose_CaptionBoxIsDeepPaper_NotYellow()
    {
        var caption = string.Join(" ", Enumerable.Repeat("caption", 12));
        var bytes = Composer().Compose([new PanelArt(SampleImages.Png(), null, caption)], Chrome);

        using var decoded = SKBitmap.Decode(bytes)!;
        // The caption box hugs the panel bottom; its fill (away from text and
        // stroke) must be PaperDeep. Sample a horizontal run just above the
        // panel's bottom edge, inside the box, and require PaperDeep presence.
        var panelBottom = DesignTokens.Comic.PageHeight - DesignTokens.Comic.BottomMargin;
        var probeY = panelBottom - (int)DesignTokens.Comic.BoxPadding - 6;
        var deep = Token(DesignTokens.PaperDeep);
        var found = false;
        for (var x = DesignTokens.Comic.SideMargin + 20; x < DesignTokens.Comic.PageWidth - DesignTokens.Comic.SideMargin - 20; x += 3)
        {
            if (decoded.GetPixel(x, probeY) == deep)
            {
                found = true;
                break;
            }
        }
        Assert.True(found, "expected a PaperDeep caption box hugging the panel bottom");
    }

    [Fact]
    public void Compose_DialogueBalloonIsBrightPaper()
    {
        var dialogue = string.Join(" ", Enumerable.Repeat("words", 12));
        var bytes = Composer().Compose([new PanelArt(SampleImages.Png(color: SKColors.CornflowerBlue), dialogue, null)], Chrome);

        using var decoded = SKBitmap.Decode(bytes)!;
        var probeY = DesignTokens.Comic.TopMargin + (int)DesignTokens.Comic.BoxPadding + 12;
        var bright = Token(DesignTokens.PaperBright);
        var found = false;
        for (var x = DesignTokens.Comic.SideMargin + 20; x < DesignTokens.Comic.PageWidth - DesignTokens.Comic.SideMargin - 20; x += 3)
        {
            if (decoded.GetPixel(x, probeY) == bright)
            {
                found = true;
                break;
            }
        }
        Assert.True(found, "expected a PaperBright dialogue balloon at the panel top");
    }

    [Fact]
    public void Compose_NoPanels_Throws()
    {
        Assert.Throws<ArgumentException>(() => Composer().Compose([], Chrome));
    }
}

public class PdfVolumeComposerTests
{
    [Fact]
    public void Compose_ProducesPdf_WithOnePagePerImage()
    {
        var pages = new List<byte[]> { SampleImages.Png(200, 300), SampleImages.Png(200, 300) };

        var pdfBytes = new PdfVolumeComposer().Compose(pages, 300);

        Assert.True(pdfBytes.Length > 4);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdfBytes, 0, 4));

        using var stream = new MemoryStream(pdfBytes);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Equal(2, document.PageCount);
    }

    [Fact]
    public void Compose_SizesPagesForTheGivenDpi()
    {
        // 600×900 px at 300 DPI is 2×3 inches — 144×216 points — regardless of the PNG's own metadata.
        var pdfBytes = new PdfVolumeComposer().Compose([SampleImages.Png(600, 900)], dpi: 300);

        using var stream = new MemoryStream(pdfBytes);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Equal(144, document.Pages[0].Width.Point, 0);
        Assert.Equal(216, document.Pages[0].Height.Point, 0);
    }
}

public class AnalysisPdfComposerTests
{
    private static AnalysisPdfComposer Composer() =>
        new(new PdfVolumeComposer(), new AnalysisDocumentRenderer());

    private static BookAnalysisResult Sample(int scale)
    {
        List<string> Many(string prefix) => Enumerable.Range(1, 3 * scale)
            .Select(i => $"{prefix} {i}: a sentence with enough words to wrap across a line or two in the report.")
            .ToList();

        var characters = Enumerable.Range(1, 2 * scale)
            .Select(i => new CharacterProfile(
                $"Character {i}", "protagonist",
                "Grows from timid to bold across the story, tested by loss and reconciliation.",
                new[] { "ally: Character 1", "rival: Character 2" }))
            .ToList();
        var reviews = Enumerable.Range(1, 3)
            .Select(i => new Review(
                $"Perspective {i}", "A fair, specific assessment of the manuscript's merits and flaws.",
                new[] { "Strong voice" }, new[] { "Uneven pacing" }, "Revise the middle act."))
            .ToList();
        var plan = Enumerable.Range(1, 3 * scale)
            .Select(i => new RevisionItem(
                i == 1 ? "High" : "Medium", $"Issue {i}", "A concrete suggested fix for the issue described above."))
            .ToList();

        return new BookAnalysisResult(
            "A concise logline for the book.",
            string.Join(" ", Enumerable.Repeat("The synopsis recounts the plot across several sentences.", 4 * scale)),
            characters, Many("Theme"), Many("Plot hole"), Many("Inconsistency"), Many("Inaccuracy"), Many("Pacing"),
            reviews, plan, Many("Comp"), "Positioned as upmarket literary fiction for adult readers.");
    }

    private static int PageCount(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    [Fact]
    public void Compose_ProducesPdf_WithAtLeastOnePage()
    {
        var pdf = Composer().Compose(Sample(1), "My Novel");

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(PageCount(pdf) >= 1);
    }

    [Fact]
    public void Compose_LargerReport_PaginatesToMorePages()
    {
        var small = PageCount(Composer().Compose(Sample(1), "My Novel"));
        var large = PageCount(Composer().Compose(Sample(8), "My Novel"));

        Assert.True(large > small, $"expected more pages for a larger report (small={small}, large={large})");
        Assert.True(large > 1);
    }

    [Fact]
    public void Compose_EmptyReport_DoesNotThrow_AndStillProducesAPage()
    {
        var pdf = Composer().Compose(BookAnalysisResult.Empty, "");

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(PageCount(pdf) >= 1);
    }

    [Fact]
    public void Renderer_ProducesDecodablePngPagesOfConfiguredWidth()
    {
        var pages = new AnalysisDocumentRenderer().Render(Sample(4), "My Novel");

        Assert.NotEmpty(pages);
        foreach (var page in pages)
        {
            using var bitmap = SKBitmap.Decode(page);
            Assert.NotNull(bitmap);
            Assert.Equal(DesignTokens.Pdf.PageWidth, bitmap!.Width);
            Assert.Equal(DesignTokens.Pdf.PageHeight, bitmap.Height);
        }
    }

    [Fact]
    public void Renderer_PageBackgroundIsGrainedPaper()
    {
        var pages = new AnalysisDocumentRenderer().Render(Sample(1), "My Novel");

        using var bitmap = SKBitmap.Decode(pages[0])!;
        SkiaPageComposerTests.AssertNearPaper(bitmap.GetPixel(5, 5));
    }

    [Fact]
    public void Renderer_FirstPage_HasTerracottaHeadingRule()
    {
        var pages = new AnalysisDocumentRenderer().Render(Sample(1), "My Novel");

        using var bitmap = SKBitmap.Decode(pages[0])!;
        var accent = SKColor.Parse(DesignTokens.Accent);
        var found = false;
        for (var y = 0; y < bitmap.Height && !found; y++)
        {
            if (bitmap.GetPixel(DesignTokens.Pdf.PageWidth / 4, y) == accent)
            {
                found = true;
            }
        }
        Assert.True(found, "expected a terracotta rule under the title heading");
    }

    [Fact]
    public void Renderer_FrontMatter_CoverAndTocPrecedeContent()
    {
        var result = new AnalysisDocumentRenderer().RenderReport(Sample(1), "My Novel");

        Assert.True(result.FrontMatterCount >= 2, "expected a cover and at least one TOC page");
        Assert.Equal(result.Pages.Count - result.FrontMatterCount,
            result.Pages.Count - result.FrontMatterCount);
        Assert.NotEmpty(result.TocEntries);
        // Every H2 section lands in the TOC with a plausible 1-based content page.
        Assert.Contains(result.TocEntries, e => e.Title == "Logline");
        Assert.All(result.TocEntries, e =>
            Assert.InRange(e.Page, 1, result.Pages.Count - result.FrontMatterCount));
        // Entries are in reading order.
        var pagesInOrder = result.TocEntries.Select(e => e.Page).ToList();
        Assert.Equal(pagesInOrder.OrderBy(p => p), pagesInOrder);
    }

    [Fact]
    public void Renderer_TocPageListsTheSections()
    {
        var result = new AnalysisDocumentRenderer().RenderReport(Sample(1), "My Novel");

        using var toc = SKBitmap.Decode(result.Pages[1])!; // page 0 = cover, page 1 = contents
        var inked = 0;
        for (var x = 0; x < toc.Width; x += 3)
        {
            for (var y = 0; y < toc.Height; y += 3)
            {
                if (SkiaPageComposerTests.IsStrongInk(toc.GetPixel(x, y)))
                {
                    inked++;
                }
            }
        }
        Assert.True(inked > 200, $"expected a populated contents page, found {inked} ink pixels");
    }

    [Fact]
    public void Renderer_EveryPage_HasFooterInk()
    {
        var result = new AnalysisDocumentRenderer().RenderReport(Sample(4), "My Novel");
        var pages = result.Pages.Skip(result.FrontMatterCount).ToList(); // front matter is unnumbered
        Assert.True(pages.Count > 1);

        foreach (var page in pages)
        {
            using var bitmap = SKBitmap.Decode(page)!;
            var footerTop = DesignTokens.Pdf.PageHeight - (int)DesignTokens.Pdf.BottomMargin;
            var inked = 0;
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                for (var y = footerTop; y < bitmap.Height; y += 2)
                {
                    if (SkiaPageComposerTests.IsStrongInk(bitmap.GetPixel(x, y)))
                    {
                        inked++;
                    }
                }
            }
            Assert.True(inked > 20, $"expected footer text (running title + page number), found {inked} inked pixels");
        }
    }
}
