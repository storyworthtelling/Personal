using System.Text.Json;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.Design;
using Storyboarder.Domain.Imaging;
using Storyboarder.Domain.Model;
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
    public void Compose_SpeechBalloon_HasATailPointingDown()
    {
        var dialogue = string.Join(" ", Enumerable.Repeat("words", 12));
        var bytes = Composer().Compose(
            [new PanelArt(SampleImages.Png(color: SKColors.CornflowerBlue), dialogue, null)], Chrome);

        using var decoded = SKBitmap.Decode(bytes)!;
        var bright = Token(DesignTokens.PaperBright);

        // Lowest bright-paper pixel in a column ~75% across the balloon (no tail there): the balloon bottom.
        int LowestBright(int x)
        {
            var lowest = -1;
            for (var y = DesignTokens.Comic.TopMargin; y < DesignTokens.Comic.PageHeight / 3; y++)
                if (decoded.GetPixel(x, y) == bright)
                    lowest = y;
            return lowest;
        }

        var balloonBottom = LowestBright((int)(DesignTokens.Comic.PageWidth * 0.75));
        // The tail is drawn ~40% across the balloon, so its bright fill reaches deeper than the flat bottom.
        var tailBottom = LowestBright((int)(DesignTokens.Comic.PageWidth * 0.42));

        Assert.True(balloonBottom > 0 && tailBottom > balloonBottom,
            $"expected the tail column ({tailBottom}) to reach below the balloon bottom ({balloonBottom})");
    }

    [Fact]
    public void Compose_ThoughtBalloon_RendersDifferentlyThanSpeech_WithBubblesBelow()
    {
        var dialogue = string.Join(" ", Enumerable.Repeat("words", 12));
        var speech = Composer().Compose(
            [new PanelArt(SampleImages.Png(), dialogue, null, ShotType.Standard, DialogueKind.Speech)], Chrome);
        var thought = Composer().Compose(
            [new PanelArt(SampleImages.Png(), dialogue, null, ShotType.Standard, DialogueKind.Thought)], Chrome);

        // A pointed tail and a bubble trail are different marks, so the pages differ.
        Assert.NotEqual(speech, thought);

        // The trail places bright bubbles below the balloon (~30–55% across); assert bright paper appears
        // strictly below the balloon's flat bottom there.
        using var decoded = SKBitmap.Decode(thought)!;
        var bright = Token(DesignTokens.PaperBright);
        // Balloon bottom from a column solidly inside the box (60% across), away from the trail.
        var balloonBottom = -1;
        var probe = (int)(DesignTokens.Comic.PageWidth * 0.6);
        for (var y = DesignTokens.Comic.TopMargin; y < DesignTokens.Comic.PageHeight / 3; y++)
            if (decoded.GetPixel(probe, y) == bright)
                balloonBottom = y;

        var bubbleFound = false;
        for (var y = balloonBottom + 2; y <= balloonBottom + 80 && !bubbleFound; y++)
            for (var x = (int)(DesignTokens.Comic.PageWidth * 0.30); x <= (int)(DesignTokens.Comic.PageWidth * 0.55); x++)
                if (decoded.GetPixel(x, y) == bright)
                {
                    bubbleFound = true;
                    break;
                }
        Assert.True(balloonBottom > 0 && bubbleFound,
            $"expected thought bubbles below the balloon bottom ({balloonBottom})");
    }

    [Fact]
    public void Compose_Sfx_LettersTerracottaOverTheArt()
    {
        var accent = Token(DesignTokens.Accent);
        int AccentPixels(byte[] png)
        {
            using var b = SKBitmap.Decode(png)!;
            var n = 0;
            for (var x = 0; x < b.Width; x += 3)
                for (var y = 0; y < b.Height; y += 3)
                    if (b.GetPixel(x, y) == accent)
                        n++;
            return n;
        }

        var withSfx = Composer().Compose(
            [new PanelArt(SampleImages.Png(color: SKColors.CornflowerBlue), null, null, ShotType.Standard, DialogueKind.Speech, "BOOM")],
            Chrome);
        var withoutSfx = Composer().Compose(
            [new PanelArt(SampleImages.Png(color: SKColors.CornflowerBlue), null, null)], Chrome);

        // The page composer's own palette has no terracotta, so accent pixels are the SFX lettering.
        Assert.Equal(0, AccentPixels(withoutSfx));
        Assert.True(AccentPixels(withSfx) > 0, "expected terracotta SFX lettering over the art");
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

    [Fact]
    public void AppendPage_BuildsMultiPagePdf_AcrossSeparateCalls()
    {
        // Assembling a volume one page per call must yield the same multi-page PDF that a single
        // Compose would — this is what lets the pipeline resume mid-volume instead of holding every
        // page in memory at once.
        var composer = new PdfVolumeComposer();

        var pdf = composer.AppendPage(null, SampleImages.Png(200, 300), 300);
        pdf = composer.AppendPage(pdf, SampleImages.Png(200, 300), 300);
        pdf = composer.AppendPage(pdf, SampleImages.Png(200, 300), 300);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));

        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Equal(3, document.PageCount);
    }

    [Fact]
    public void AppendPage_SizesEachPageForTheGivenDpi()
    {
        // Each appended page is sized from its raster's pixels at the requested DPI, exactly as Compose:
        // 600×900 px at 300 DPI is 2×3 inches — 144×216 points.
        var composer = new PdfVolumeComposer();
        var pdf = composer.AppendPage(null, SampleImages.Png(600, 900), dpi: 300);

        using var stream = new MemoryStream(pdf);
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

        List<Finding> ManyFindings(string prefix) => Enumerable.Range(1, 3 * scale)
            .Select(i => new Finding(
                $"Chapter {i}",
                $"{prefix} {i}: a sentence with enough words to wrap across a line or two in the report."))
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
                i == 1 ? "High" : "Medium", $"Chapter {i}", $"Issue {i}",
                "A concrete suggested fix for the issue described above."))
            .ToList();
        var lineEdits = Enumerable.Range(1, 3 * scale)
            .Select(i => new LineEdit(
                $"Chapter {i}", $"He felt the cold creeping in, and it was then that he knew, in that moment {i}.",
                "Cut to: The cold crept in.", "Filtered through the viewpoint and padded with throat-clearing."))
            .ToList();
        var proseNotes = Enumerable.Range(1, 3 * scale)
            .Select(i => new ProseNote(
                $"Chapter {i}", "Filter words", "\"she felt\", \"he saw\", \"it seemed\" recur throughout.",
                "Delete the filter and let the sensation land directly."))
            .ToList();
        var dialogueNotes = Enumerable.Range(1, 3 * scale)
            .Select(i => new DialogueNote(
                $"Chapter {i}", "Character 1, Character 2", "The exchange states the theme outright.",
                "Let the subtext carry it; cut the last two lines."))
            .ToList();
        var scenes = Enumerable.Range(1, 3 * scale)
            .Select(i => new SceneBeat(
                $"Chapter {i}", $"Scene {i}: the hero arrives at the gate and waits.",
                "Establishes the setting and stalls the confrontation.",
                i % 3 == 0 ? "Does no work: the same beat is played two scenes earlier." : "Earns its place."))
            .ToList();

        return new BookAnalysisResult(
            "A concise logline for the book.",
            string.Join(" ", Enumerable.Repeat("The synopsis recounts the plot across several sentences.", 4 * scale)),
            characters, Many("Theme"), ManyFindings("Plot hole"), ManyFindings("Inconsistency"),
            ManyFindings("Inaccuracy"), ManyFindings("Pacing"),
            reviews, plan, Many("Comp"), "Positioned as upmarket literary fiction for adult readers.",
            lineEdits, proseNotes, dialogueNotes, scenes);
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
    public void Renderer_TocIncludesTheCraftSections()
    {
        var result = new AnalysisDocumentRenderer().RenderReport(Sample(1), "My Novel");
        var titles = result.TocEntries.Select(entry => entry.Title).ToList();

        // The four dimensions the owner asked for are rendered AND findable from the contents page.
        Assert.Contains("Line-level edits", titles);
        Assert.Contains("Prose style & voice", titles);
        Assert.Contains("Dialogue", titles);
        Assert.Contains("Scene-by-scene breakdown", titles);
    }

    [Fact]
    public void Renderer_LegacyReportWithoutCraftSectionsOrCitations_StillRenders()
    {
        // A report consolidated by an earlier version: findings are bare strings and the craft lists
        // are absent entirely, so they deserialize as null. Re-rendering one must not throw.
        const string legacy =
            "{\"Logline\":\"A girl quests.\",\"Synopsis\":\"She grows up.\",\"Characters\":[]," +
            "\"Themes\":[\"courage\"],\"PlotHoles\":[\"Where is the key?\"],\"Inconsistencies\":[]," +
            "\"Inaccuracies\":[],\"PacingNotes\":[\"Middle sags\"],\"Reviews\":[],\"RevisionPlan\":" +
            "[{\"Priority\":\"High\",\"Issue\":\"Sagging middle\",\"SuggestedFix\":\"Cut a subplot.\"}]," +
            "\"ComparableTitles\":[],\"MarketPositioning\":\"YA fantasy\"}";
        var report = JsonSerializer.Deserialize<BookAnalysisResult>(
            legacy, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal("Where is the key?", Assert.Single(report.PlotHoles).Detail);
        Assert.Equal(string.Empty, Assert.Single(report.PlotHoles).Chapter);
        Assert.Null(report.LineEdits);

        var pages = new AnalysisDocumentRenderer().Render(report, "My Novel");

        Assert.NotEmpty(pages);
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
