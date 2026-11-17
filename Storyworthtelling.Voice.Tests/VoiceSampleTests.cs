using Storyworthtelling.Voice;
using Xunit;

namespace Storyworthtelling.Voice.Tests;

/// <summary>
/// What of a manuscript the voice is learned from (audit W10.6). It used to be the first twelve thousand characters,
/// which is a title page, a dedication and the opening chapter: the voice of a book's first pages, not the book.
/// </summary>
public class VoiceSampleTests
{
    private static string Book(int paragraphs) =>
        string.Join("\n\n", Enumerable.Range(1, paragraphs).Select(n => $"Paragraph {n} says something plain about the tide."));

    [Fact]
    public void AShortTextIsTakenWhole()
    {
        var text = Book(5);

        Assert.Equal(text, VoiceSample.Of(text));
    }

    [Fact]
    public void ALongTextIsSampledFromBeginningToEnd()
    {
        var text = Book(4000);

        var sample = VoiceSample.Of(text, budget: 4000);

        Assert.True(sample.Length <= 4000 + 3 * VoiceSample.Separator.Length);
        Assert.Contains("Paragraph 1 ", sample);
        Assert.Contains("Paragraph 4000 ", sample);
        Assert.Contains("Paragraph 2", sample[(sample.Length / 3)..(2 * sample.Length / 3)]);
    }

    [Fact]
    public void AWindowStartsAndEndsOnAWordsEdge()
    {
        var text = Book(4000);

        var sample = VoiceSample.Of(text, budget: 4000);

        foreach (var window in sample.Split(VoiceSample.Separator))
        {
            var at = text.IndexOf(window, StringComparison.Ordinal);
            Assert.True(at >= 0, "a window is not a run of the text");
            Assert.True(at == 0 || char.IsWhiteSpace(text[at - 1]), "a window began mid-word");
            var end = at + window.Length;
            Assert.True(end == text.Length || char.IsWhiteSpace(text[end]), "a window ended mid-word");
        }
    }

    [Fact]
    public void AnEmptyTextIsEmpty() => Assert.Equal(string.Empty, VoiceSample.Of("   "));
}
