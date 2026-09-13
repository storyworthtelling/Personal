using Folio.Domain.Text;
using Xunit;

namespace Folio.Tests.Text;

public class ChapterDetectorTests
{
    private static readonly ChapterDetector Detector = new();

    // Every fixture must satisfy the two invariants the rest of the pipeline relies on: at least one
    // chapter, and spans that tile the text exactly (no gaps, no overlaps, nothing past the end).
    private static void AssertCoversText(IReadOnlyList<ChapterSpan> chapters, string text)
    {
        Assert.NotEmpty(chapters);
        Assert.Equal(0, chapters[0].StartOffset);
        Assert.Equal(text.Length, chapters[^1].EndOffset);
        for (int i = 0; i < chapters.Count; i++)
        {
            Assert.Equal(i + 1, chapters[i].Number);
            Assert.True(chapters[i].EndOffset > chapters[i].StartOffset,
                $"chapter {chapters[i].Number} is empty ({chapters[i].StartOffset}..{chapters[i].EndOffset})");
            if (i > 0)
                Assert.Equal(chapters[i - 1].EndOffset, chapters[i].StartOffset);
        }
    }

    private static string Body(string label) =>
        $"{label} began quietly, and the hero walked on through the long grey hall toward the gate.";

    [Fact]
    public void NoHeadings_SynthesizesOneChapterCoveringTheWholeText()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 6).Select(i => Body($"Paragraph {i}")));

        var chapters = Detector.Detect(text);

        var only = Assert.Single(chapters);
        Assert.Equal(1, only.Number);
        Assert.Equal(ChapterDetector.WholeManuscriptLabel, only.Label);
        AssertCoversText(chapters, text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n \t ")]
    public void EmptyOrWhitespace_StillProducesOneChapter(string text)
    {
        var chapters = Detector.Detect(text);

        var only = Assert.Single(chapters);
        Assert.Equal(1, only.Number);
        Assert.Equal(0, only.StartOffset);
        Assert.Equal(text.Length, only.EndOffset);
    }

    [Fact]
    public void NumberedChapterHeadings_AreDetected()
    {
        var text =
            "Chapter 1\n\n" + Body("It") + "\n\n" +
            "Chapter 2\n\n" + Body("It") + "\n\n" +
            "Chapter 3\n\n" + Body("It");

        var chapters = Detector.Detect(text);

        Assert.Equal(3, chapters.Count);
        Assert.Equal(["Chapter 1", "Chapter 2", "Chapter 3"], chapters.Select(c => c.Label));
        AssertCoversText(chapters, text);
    }

    [Fact]
    public void SpelledOutAndDecoratedChapterHeadings_AreDetected()
    {
        var text =
            "CHAPTER ONE\n\n" + Body("It") + "\n\n" +
            "Chapter Twenty-One — The Long Road\n\n" + Body("It") + "\n\n" +
            "Chapter XII: A Reckoning\n\n" + Body("It");

        var chapters = Detector.Detect(text);

        Assert.Equal(3, chapters.Count);
        Assert.Equal("CHAPTER ONE", chapters[0].Label);
        Assert.Equal("Chapter Twenty-One — The Long Road", chapters[1].Label);
        Assert.Equal("Chapter XII: A Reckoning", chapters[2].Label);
    }

    [Fact]
    public void PrologueAndEpilogue_AreChapters()
    {
        var text =
            "Prologue\n\n" + Body("It") + "\n\n" +
            "Chapter 1\n\n" + Body("It") + "\n\n" +
            "Epilogue\n\n" + Body("It");

        var chapters = Detector.Detect(text);

        Assert.Equal(["Prologue", "Chapter 1", "Epilogue"], chapters.Select(c => c.Label));
        // Number is reading order, so the prologue is chapter 1 even though the book calls the next one 1.
        Assert.Equal(1, chapters[0].Number);
        AssertCoversText(chapters, text);
    }

    [Fact]
    public void RomanNumeralsOnTheirOwnLine_ChapterTheBook()
    {
        var text =
            "I\n\n" + Body("It") + "\n\n" +
            "II\n\n" + Body("It") + "\n\n" +
            "III\n\n" + Body("It");

        var chapters = Detector.Detect(text);

        Assert.Equal(["I", "II", "III"], chapters.Select(c => c.Label));
        AssertCoversText(chapters, text);
    }

    [Fact]
    public void BareNumericLines_ChapterTheBook()
    {
        var text =
            "1\n\n" + Body("It") + "\n\n" +
            "2.\n\n" + Body("It") + "\n\n" +
            "3\n\n" + Body("It");

        var chapters = Detector.Detect(text);

        Assert.Equal(["1", "2.", "3"], chapters.Select(c => c.Label));
        AssertCoversText(chapters, text);
    }

    [Fact]
    public void StandaloneAllCapsLines_ChapterTheBook()
    {
        var text =
            "THE LONG ROAD\n\n" + Body("It") + "\n\n" +
            "A COLD WELCOME\n\n" + Body("It") + "\n\n" +
            "THE GATE\n\n" + Body("It");

        var chapters = Detector.Detect(text);

        Assert.Equal(["THE LONG ROAD", "A COLD WELCOME", "THE GATE"], chapters.Select(c => c.Label));
        AssertCoversText(chapters, text);
    }

    [Fact]
    public void SeparatorsOnly_SplitIntoNumberedSections()
    {
        var text =
            Body("It") + "\n\n" +
            "* * *\n\n" + Body("It") + "\n\n" +
            "***\n\n" + Body("It");

        var chapters = Detector.Detect(text);

        Assert.Equal(3, chapters.Count);
        // The manuscript never named these, so the labels are synthesized from the reading order.
        Assert.Equal(ChapterDetector.OpeningLabel, chapters[0].Label);
        Assert.Equal("Section 2", chapters[1].Label);
        Assert.Equal("Section 3", chapters[2].Label);
        AssertCoversText(chapters, text);
    }

    [Fact]
    public void TextBeforeTheFirstHeading_BecomesAnOpeningChapter()
    {
        var text =
            "A Novel\nby Someone\n\n" + Body("Front matter") + "\n\n" +
            "Chapter 1\n\n" + Body("It") + "\n\n" +
            "Chapter 2\n\n" + Body("It");

        var chapters = Detector.Detect(text);

        Assert.Equal(3, chapters.Count);
        Assert.Equal(ChapterDetector.OpeningLabel, chapters[0].Label);
        Assert.Equal("Chapter 1", chapters[1].Label);
        AssertCoversText(chapters, text);
    }

    [Fact]
    public void HeadingAtTheVeryStart_ProducesNoEmptyOpeningChapter()
    {
        var text = "Chapter 1\n\n" + Body("It") + "\n\nChapter 2\n\n" + Body("It");

        var chapters = Detector.Detect(text);

        Assert.Equal(2, chapters.Count);
        Assert.Equal("Chapter 1", chapters[0].Label);
        Assert.Equal(0, chapters[0].StartOffset);
    }

    [Fact]
    public void ProseBeginningWithTheWordChapter_IsNotAHeading()
    {
        var text =
            "Chapter 1\n\n" +
            "Chapter openings had always defeated him, and he sat there for a long while staring at the " +
            "blank page and the cold grey light beyond the window.\n\n" +
            "Chapter 2\n\n" + Body("It");

        var chapters = Detector.Detect(text);

        Assert.Equal(2, chapters.Count);
        Assert.Equal(["Chapter 1", "Chapter 2"], chapters.Select(c => c.Label));
    }

    [Fact]
    public void ShoutedAllCapsDialogue_DoesNotChapterABookThatNamesItsChapters()
    {
        var text =
            "Chapter 1\n\n" + Body("It") + "\n\n" +
            "GET OUT!\n\n" + Body("It") + "\n\n" +
            "Chapter 2\n\n" + Body("It");

        var chapters = Detector.Detect(text);

        // Two strong headings settle it; the weak all-caps heuristic is never consulted.
        Assert.Equal(2, chapters.Count);
        Assert.Equal(["Chapter 1", "Chapter 2"], chapters.Select(c => c.Label));
    }

    [Fact]
    public void ALonePrologue_IsMergedWithTheRomanNumeralsThatFollow()
    {
        // A real book format: a named prologue, then roman-numeralled chapters. One strong heading is
        // not a chaptering scheme by itself, so the weak category is consulted and merged in.
        var text =
            "Prologue\n\n" + Body("It") + "\n\n" +
            "I\n\n" + Body("It") + "\n\n" +
            "II\n\n" + Body("It") + "\n\n" +
            "III\n\n" + Body("It");

        var chapters = Detector.Detect(text);

        Assert.Equal(["Prologue", "I", "II", "III"], chapters.Select(c => c.Label));
        AssertCoversText(chapters, text);
    }

    [Fact]
    public void ALoneChapterHeading_StillChaptersTheBook()
    {
        var text = Body("Front") + "\n\nChapter 1\n\n" + Body("It");

        var chapters = Detector.Detect(text);

        Assert.Equal(2, chapters.Count);
        Assert.Equal(ChapterDetector.OpeningLabel, chapters[0].Label);
        Assert.Equal("Chapter 1", chapters[1].Label);
    }

    [Fact]
    public void OverLongHeadingLine_IsTreatedAsProse()
    {
        var heading = "Chapter 1 " + string.Join(" ", Enumerable.Repeat("word", 30));
        var text = heading + "\n\n" + Body("It") + "\n\nChapter 2\n\n" + Body("It");

        var chapters = Detector.Detect(text);

        // The over-long line is prose-shaped, so it is not a heading at all: only "Chapter 2" is.
        Assert.Equal(2, chapters.Count);
        Assert.Equal(ChapterDetector.OpeningLabel, chapters[0].Label);
        Assert.Equal("Chapter 2", chapters[1].Label);
    }

    [Fact]
    public void Locate_ChunkInsideOneChapter_CitesThatChapter()
    {
        var text = "Chapter 1\n\n" + Body("It") + "\n\nChapter 2\n\n" + Body("It");
        var chapters = Detector.Detect(text);
        var second = chapters[1];

        var citation = ChapterDetector.Locate(chapters, second.StartOffset + 1, second.EndOffset - 1);

        Assert.Equal(2, citation.Number);
        Assert.Equal("Chapter 2", citation.Label);
    }

    [Fact]
    public void Locate_ChunkStraddlingABreak_CitesTheWholeSpan()
    {
        var text = "Chapter 1\n\n" + Body("It") + "\n\nChapter 2\n\n" + Body("It") + "\n\nChapter 3\n\n" + Body("It");
        var chapters = Detector.Detect(text);

        // A chunk running from inside chapter 1 to inside chapter 3 overlaps all three.
        var citation = ChapterDetector.Locate(chapters, 2, chapters[2].StartOffset + 3);

        Assert.Equal(1, citation.Number);
        Assert.Equal("Chapters 1–3", citation.Label);
    }

    [Fact]
    public void Locate_ChunkOutsideEveryChapter_CitesNothing()
    {
        var chapters = new List<ChapterSpan> { new(1, "Chapter 1", 0, 10) };

        var citation = ChapterDetector.Locate(chapters, 50, 60);

        Assert.Equal(0, citation.Number);
        Assert.Equal(string.Empty, citation.Label);
    }

    [Fact]
    public void Locate_EmptyChunkSpan_StillCitesTheChapterItSitsIn()
    {
        var chapters = new List<ChapterSpan> { new(1, "Chapter 1", 0, 10), new(2, "Chapter 2", 10, 20) };

        var citation = ChapterDetector.Locate(chapters, 12, 12);

        Assert.Equal(2, citation.Number);
        Assert.Equal("Chapter 2", citation.Label);
    }

    [Fact]
    public void Detection_IsDeterministic()
    {
        var text = "Chapter 1\n\n" + Body("It") + "\n\nChapter 2\n\n" + Body("It");

        Assert.Equal(Detector.Detect(text), new ChapterDetector().Detect(text));
    }

    [Fact]
    public void WindowsLineEndings_AreHandled()
    {
        var text = "Chapter 1\r\n\r\n" + Body("It") + "\r\n\r\nChapter 2\r\n\r\n" + Body("It");

        var chapters = Detector.Detect(text);

        Assert.Equal(["Chapter 1", "Chapter 2"], chapters.Select(c => c.Label));
        AssertCoversText(chapters, text);
    }
}
