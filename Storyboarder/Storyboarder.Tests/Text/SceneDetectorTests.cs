using Storyboarder.Domain.Text;
using Xunit;

namespace Storyboarder.Tests.Text;

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

    [Fact]
    public void LongUnbrokenChapter_IsSplitByLength_AtParagraphBoundaries()
    {
        // A wall-of-text chapter with no dividers still yields several scenes rather than one giant one.
        var paragraph = new string('a', 1_000) + ".\n\n";
        var text = string.Concat(Enumerable.Repeat(paragraph, 30)); // ~30K chars, no dividers
        var chapters = new[] { Chapter(1, "Chapter 1", 0, text.Length) };

        var scenes = detector.Detect(text, chapters);

        Assert.True(scenes.Count > 1, "expected a long unbroken chapter to be split by length");
        Assert.All(scenes, s => Assert.True(s.EndOffset - s.StartOffset <= SceneDetector.MaxSceneChars));
        // Still gapless.
        for (var i = 1; i < scenes.Count; i++)
            Assert.Equal(scenes[i - 1].EndOffset, scenes[i].StartOffset);
        Assert.Equal(text.Length, scenes[^1].EndOffset);
    }

    [Fact]
    public void EmptyManuscript_YieldsOneScene()
    {
        var scene = Assert.Single(detector.Detect(string.Empty, chapters: []));
        Assert.Equal(0, scene.StartOffset);
        Assert.Equal(0, scene.EndOffset);
    }
}
