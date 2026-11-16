using Folio.Domain.Text;
using Xunit;

namespace Folio.Tests.Text;

public class SceneDetectorTests
{
    private readonly SceneDetector detector = new();

    private static ChapterSpan Chapter(int number, string label, int start, int end) =>
        new(number, label, start, end);

    [Fact]
    public void SplitsOnDividerLines()
    {
        var text = "The first scene happens here.\n\n* * *\n\nThe second scene happens here.";
        var chapters = new[] { Chapter(1, "Chapter 1", 0, text.Length) };

        var scenes = detector.Detect(text, chapters);

        Assert.Equal(2, scenes.Count);
        Assert.All(scenes, s => Assert.Equal(1, s.ChapterNumber));
        Assert.Equal(1, scenes[0].Number);
        Assert.Equal(2, scenes[1].Number);
        // Gapless, contiguous tiling of the chapter.
        Assert.Equal(0, scenes[0].StartOffset);
        Assert.Equal(scenes[0].EndOffset, scenes[1].StartOffset);
        Assert.Equal(text.Length, scenes[^1].EndOffset);
    }

    [Fact]
    public void SplitsOnATwoBlankLineGap()
    {
        // A single blank line is ordinary paragraph spacing; two-plus is a scene break.
        var text = "Para one.\n\nPara two of scene one.\n\n\n\nScene two opens.";
        var chapters = new[] { Chapter(1, "Chapter 1", 0, text.Length) };

        var scenes = detector.Detect(text, chapters);

        Assert.Equal(2, scenes.Count);
    }

    [Fact]
    public void OneScene_WhenTheChapterHasNoBreaks()
    {
        var text = "A quiet, unbroken chapter.\n\nJust two paragraphs, one continuous scene.";
        var chapters = new[] { Chapter(1, "Chapter 1", 0, text.Length) };

        var scene = Assert.Single(detector.Detect(text, chapters));
        Assert.Equal(0, scene.StartOffset);
        Assert.Equal(text.Length, scene.EndOffset);
    }

    [Fact]
    public void NumbersScenesSequentially_AcrossChapters_AndCitesTheChapter()
    {
        // Two chapters, the first split by a divider, so numbering must run 1,2,3 across them.
        var chapterOne = "Opening beat.\n\n***\n\nSecond beat.";
        var text = chapterOne + "\n\nChapter Two prose.";
        var chapters = new[]
        {
            Chapter(1, "Chapter 1", 0, chapterOne.Length),
            Chapter(2, "Chapter 2", chapterOne.Length, text.Length)
        };

        var scenes = detector.Detect(text, chapters);

        Assert.Equal(3, scenes.Count);
        Assert.Equal([1, 2, 3], scenes.Select(s => s.Number).ToArray());
        Assert.Equal([1, 1, 2], scenes.Select(s => s.ChapterNumber).ToArray());
        Assert.Equal("Chapter 2", scenes[^1].ChapterLabel);
    }

    [Fact]
    public void NoChapters_TreatsTheWholeTextAsOneChapter()
    {
        var text = "Scene one.\n\n* * *\n\nScene two.";

        var scenes = detector.Detect(text, chapters: []);

        Assert.Equal(2, scenes.Count);
        Assert.All(scenes, s => Assert.Equal(1, s.ChapterNumber));
    }

    /// <summary>
    /// The owner's ruling (R2): a chapter with no breaks is one scene, however long. Scene breaks come
    /// from the author's text alone; cutting by length invented scenes the author never wrote.
    /// </summary>
    [Fact]
    public void LongUnbrokenChapter_IsOneScene()
    {
        var paragraph = new string('a', 1_000) + ".\n\n";
        var text = string.Concat(Enumerable.Repeat(paragraph, 30)); // ~30K chars, no breaks
        var chapters = new[] { Chapter(1, "Chapter 1", 0, text.Length) };

        var scene = Assert.Single(detector.Detect(text, chapters));
        Assert.Equal(0, scene.StartOffset);
        Assert.Equal(text.Length, scene.EndOffset);
    }

    [Fact]
    public void ASingleBlankLineNeverSplits()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 40).Select(i => $"Paragraph {i}."));
        var chapters = new[] { Chapter(1, "Chapter 1", 0, text.Length) };

        Assert.Single(detector.Detect(text, chapters));
    }

    [Theory]
    [InlineData("#")]
    [InlineData("##")]
    [InlineData("⁂")]
    [InlineData("~ ~ ~")]
    [InlineData("*")]
    [InlineData("* * *")]
    [InlineData("***")]
    [InlineData("---")]
    [InlineData("❧")]
    [InlineData("o o o")]
    public void DividerGlyphLinesSplit(string divider)
    {
        var text = $"Scene one ends.\n\n{divider}\n\nScene two begins.";
        var chapters = new[] { Chapter(1, "Chapter 1", 0, text.Length) };

        Assert.Equal(2, detector.Detect(text, chapters).Count);
    }

    /// <summary>A dash or two inside prose is punctuation, not a rule; it takes three to draw one.</summary>
    [Theory]
    [InlineData("-")]
    [InlineData("--")]
    [InlineData("— she said")]
    [InlineData("# Heading words")]
    [InlineData("I said no.")]
    public void OrdinaryLinesDoNotSplit(string line)
    {
        var text = $"Scene one goes on.\n\n{line}\n\nAnd on.";
        var chapters = new[] { Chapter(1, "Chapter 1", 0, text.Length) };

        Assert.Single(detector.Detect(text, chapters));
    }

    [Fact]
    public void WindowsLineEndingsAreReadTheSame()
    {
        var text = "Scene one.\r\n\r\n\r\n\r\nScene two.\r\n\r\n* * *\r\n\r\nScene three.";
        var chapters = new[] { Chapter(1, "Chapter 1", 0, text.Length) };

        Assert.Equal(3, detector.Detect(text, chapters).Count);
    }

    [Fact]
    public void EmptyManuscript_YieldsOneScene()
    {
        var scene = Assert.Single(detector.Detect(string.Empty, chapters: []));
        Assert.Equal(0, scene.StartOffset);
        Assert.Equal(0, scene.EndOffset);
    }
}
