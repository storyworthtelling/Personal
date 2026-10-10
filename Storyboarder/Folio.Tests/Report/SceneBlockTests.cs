using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Domain.Markup;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Support;
using UglyToad.PdfPig;

namespace Folio.Tests.Report;

/// <summary>
/// Scene by scene is no longer a section; each scene's picture and notes are a block inserted into the
/// marked manuscript at the end of that scene.
///
/// <para>What can go wrong without anything failing: a block drawn at the end of the book instead of
/// under its scene (every picture still prints), a block pushing the next scene onto a new page for
/// no reason, and a book with no markings dropping its scene blocks along with its marks.</para>
/// </summary>
public class SceneBlockTests
{
    private static readonly ReportCover Cover =
        new("The Salt Line", 1, 1_200, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null);

    private const string SceneOne = "Ruth came back to Saltings in the spring.\nThe water had taken her brother.";
    private const string Break = "\n\n* * *\n\n";
    private const string SceneTwo = "She walked the sea wall for an hour and nobody spoke to her.";
    private const string Manuscript = SceneOne + Break + SceneTwo;

    private static SceneEvidence One(byte[]? image = null, byte[]? previous = null) =>
        new("Chapter 1 · Scene 1", "Establishes the grief.", "Earns its place.", image, previous)
        { EndOffset = SceneOne.Length + Break.Length };

    private static SceneEvidence Two(byte[]? image = null) =>
        new("Chapter 1 · Scene 2", "Shows the village closing ranks.", "Does no work.", image)
        { EndOffset = Manuscript.Length };

    [Fact]
    public void EachScenesBlockSitsUnderThatScenesLastLine()
    {
        var text = TextOf(Render([One(), Two()]));

        var sceneOneEnds = text.IndexOf("taken her brother", StringComparison.Ordinal);
        var blockOne = text.IndexOf("Establishes the grief", StringComparison.Ordinal);
        var sceneTwoStarts = text.IndexOf("walked the sea wall", StringComparison.Ordinal);
        var blockTwo = text.IndexOf("Shows the village", StringComparison.Ordinal);

        Assert.True(sceneOneEnds >= 0 && sceneOneEnds < blockOne, "Block one should follow scene one's prose.");
        Assert.True(blockOne < sceneTwoStarts, "Block one should come before scene two begins.");
        Assert.True(sceneTwoStarts < blockTwo, "Block two should follow scene two's prose.");
    }

    [Fact]
    public void ABlockCarriesTheCitationThePurposeAndTheVerdict()
    {
        var text = TextOf(Render([One()]));

        Assert.Contains("Chapter 1 · Scene 1", text, StringComparison.Ordinal);
        Assert.Contains("Establishes the grief.", text, StringComparison.Ordinal);
        Assert.Contains("Earns its place.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockPrintsItsPictureAndAChangedSceneItsBeforeAndAfter()
    {
        var single = Pictures(Render([One(SampleImages.Png())]));
        var paired = Pictures(Render([One(SampleImages.Png(), SampleImages.Png())]));

        Assert.Equal(1, single);
        Assert.Equal(2, paired);
    }

    [Fact]
    public void ASceneWithNoPictureSaysSoRatherThanLeavingAGap()
    {
        Assert.Contains("Image unavailable for this scene.", TextOf(Render([One()])), StringComparison.Ordinal);
    }

    /// <summary>A block is small next to a sheet; the next scene carries on beneath it, not on a fresh page.</summary>
    [Fact]
    public void TheNextSceneContinuesOnTheSamePageWhenThereIsRoom()
    {
        var pages = PagesOf(Render([One(), Two()]));

        var page = pages.Single(p => p.Contains("Establishes the grief", StringComparison.Ordinal));

        Assert.Contains("walked the sea wall", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A book nothing was written on still has its scenes: blocks are content, and the marks they
    /// share the page with are not what makes the manuscript worth printing.
    /// </summary>
    [Fact]
    public void ABookWithNoMarksStillPrintsItsManuscriptForTheSceneBlocks()
    {
        var text = TextOf(Render([One(), Two()], SampleAnalysis.Of()));

        Assert.Contains("taken her brother", text, StringComparison.Ordinal);
        Assert.Contains("Establishes the grief.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoMarksAndNoScenesThereIsNoManuscript()
    {
        var text = TextOf(Render([], SampleAnalysis.Of()));

        Assert.DoesNotContain("taken her brother", text, StringComparison.Ordinal);
    }

    /// <summary>The marks and their notes are still drawn on the same pages the blocks are.</summary>
    [Fact]
    public void MarksStillSitBesideTheProseWhenBlocksAreInserted()
    {
        var analysis = SampleAnalysis.Of() with
        {
            LineEdits = [new LineEdit("Chapter 1", "nobody spoke to her", "x", "Everyone is silent.")],
        };

        var text = TextOf(Render([One(), Two()], analysis));

        Assert.Contains("silent", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoManuscriptTheScenesStillPrint()
    {
        var pdf = new AnalysisDocumentRenderer().Render(
            SampleAnalysis.Of(), Cover, [One(SampleImages.Png()), Two()]);

        Assert.Contains("Establishes the grief.", TextOf(pdf), StringComparison.Ordinal);
        Assert.Equal(1, Pictures(pdf));
    }

    /// <summary>An evidence record with no end offset is not lost; it follows the last line.</summary>
    [Fact]
    public void ASceneWithNoKnownEndFollowsTheEndOfTheText()
    {
        var unknown = new SceneEvidence("Chapter 9 · Scene 9", "Orphan purpose.", "Orphan verdict.", null);
        var text = TextOf(Render([unknown]));

        Assert.True(
            text.IndexOf("Orphan purpose", StringComparison.Ordinal) > text.IndexOf("nobody spoke", StringComparison.Ordinal));
    }

    private static byte[] Render(IReadOnlyList<SceneEvidence> scenes, BookAnalysisResult? analysis = null) =>
        new AnalysisDocumentRenderer().Render(
            analysis ?? SampleAnalysis.Of() with
            {
                LineEdits = [new LineEdit("Chapter 1", "Ruth came back", "x", "A flat opening.")],
            },
            Cover, scenes, review: null, Manuscript, chapters: null);

    private static int Pictures(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return document.GetPages().Sum(page => page.GetImages().Count());
    }

    private static List<string> PagesOf(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return [.. document.GetPages().Select(page => page.Text)];
    }

    private static string TextOf(byte[] pdf) => string.Concat(PagesOf(pdf));
}
