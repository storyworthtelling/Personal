using Folio.Domain.Text;
using Xunit;

namespace Folio.Tests.Text;

public class TextChunkerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   \n\n  \t ")]
    public void EmptyOrWhitespace_ProducesNoChunks(string text)
    {
        var chunks = new TextChunker().Chunk(text);
        Assert.Empty(chunks);
    }

    [Fact]
    public void SingleShortParagraph_ProducesOneChunk()
    {
        const string text = "The cat sat on the mat.";
        var chunks = new TextChunker().Chunk(text);

        var chunk = Assert.Single(chunks);
        Assert.Equal(0, chunk.Ordinal);
        Assert.Equal(text, chunk.Text);
    }

    [Fact]
    public void Offsets_ReconstructEachChunkText()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 8).Select(i => $"Paragraph number {i}."));
        var chunks = new TextChunker(targetChars: 50, maxChars: 100).Chunk(text);

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
            Assert.Equal(text.Substring(chunk.StartOffset, chunk.EndOffset - chunk.StartOffset), chunk.Text);
    }

    [Fact]
    public void PacksMultipleParagraphs_BelowTarget()
    {
        // Six ~17-char paragraphs; with a 60-char target several pack into one chunk.
        var text = string.Join("\n\n", Enumerable.Range(1, 6).Select(i => $"Short paragraph {i}"));
        var chunks = new TextChunker(targetChars: 60, maxChars: 120).Chunk(text);

        Assert.True(chunks.Count < 6, "Paragraphs should have been packed together.");
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 120));
    }

    [Fact]
    public void Ordinals_AreSequential()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 10).Select(i => new string('x', 40) + i));
        var chunks = new TextChunker(targetChars: 50, maxChars: 60).Chunk(text);

        for (int i = 0; i < chunks.Count; i++)
            Assert.Equal(i, chunks[i].Ordinal);
    }

    [Fact]
    public void OversizedParagraph_IsSplitBelowMax()
    {
        // One paragraph far larger than maxChars, with sentence boundaries to cut on.
        var sentence = "This is a sentence that ends here. ";
        var text = string.Concat(Enumerable.Repeat(sentence, 100)); // ~3500 chars, no blank lines
        var chunks = new TextChunker(targetChars: 400, maxChars: 500).Chunk(text);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 500, $"chunk len {c.Text.Length} exceeded max"));
    }

    [Fact]
    public void HardSplits_WhenNoBoundaryExists()
    {
        var text = new string('a', 1000); // no whitespace or sentence enders at all
        var chunks = new TextChunker(targetChars: 200, maxChars: 250).Chunk(text);

        Assert.True(chunks.Count >= 4);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 250));
    }

    [Fact]
    public void ChapterStart_ForcesABoundary_EvenWhenBothChaptersFitTogether()
    {
        // Two short chapters would pack into one chunk by size alone. A chapter is the unit the analysis
        // reads and cites, so a chunk must never run from one chapter into the next.
        const string text = "Chapter 1\n\nA short opening.\n\nChapter 2\n\nA short second.";
        int secondStart = text.IndexOf("Chapter 2", StringComparison.Ordinal);

        var chunks = new TextChunker().Chunk(text, [secondStart]);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(0, chunks[0].StartOffset);
        Assert.Equal(secondStart, chunks[1].StartOffset);
        Assert.StartsWith("Chapter 1", chunks[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("Chapter 2", chunks[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ChapterStarts_OneChunkPerChapter_WhenEachChapterIsShort()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 40).Select(n => $"Chapter {n}\n\nA line of prose for chapter {n}."));
        var starts = Enumerable.Range(1, 40)
            .Select(n => text.IndexOf($"Chapter {n}\n", StringComparison.Ordinal))
            .ToArray();

        var chunks = new TextChunker().Chunk(text, starts);

        Assert.Equal(40, chunks.Count);
        Assert.Equal(starts, chunks.Select(c => c.StartOffset).ToArray());
    }

    [Fact]
    public void ChapterStartAtTheVeryStart_DoesNotProduceAnEmptyChunk()
    {
        const string text = "Chapter 1\n\nProse.";

        var chunks = new TextChunker().Chunk(text, [0]);

        var chunk = Assert.Single(chunks);
        Assert.Equal(text, chunk.Text);
    }
}
