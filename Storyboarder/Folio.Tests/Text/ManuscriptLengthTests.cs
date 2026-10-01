using Folio.Domain.Text;

namespace Folio.Tests.Text;

/// <summary>
/// What a manuscript's own word count says it is — the bug this reintroduces is a prompt calling a
/// 1,200-word short story "the complete novel manuscript", which is where the first real device run's
/// section/pacing findings went wrong: a one-chapter story judged by novel-scale assumptions (chapters
/// that "slacken", a theme "hierarchy") has nothing to say and invents an answer instead.
/// </summary>
public class ManuscriptLengthTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1_209)]
    [InlineData(9_999)]
    public void AShortManuscriptClassifiesAsAShortStory(int wordCount)
    {
        Assert.Equal(StoryLength.ShortStory, ManuscriptLength.Classify(wordCount));
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(25_000)]
    [InlineData(39_999)]
    public void AMidLengthManuscriptClassifiesAsANovella(int wordCount)
    {
        Assert.Equal(StoryLength.Novella, ManuscriptLength.Classify(wordCount));
    }

    [Theory]
    [InlineData(40_000)]
    [InlineData(94_200)]
    [InlineData(250_000)]
    public void ALongManuscriptClassifiesAsANovel(int wordCount)
    {
        Assert.Equal(StoryLength.Novel, ManuscriptLength.Classify(wordCount));
    }

    [Theory]
    [InlineData(StoryLength.ShortStory, "short story")]
    [InlineData(StoryLength.Novella, "novella")]
    [InlineData(StoryLength.Novel, "novel")]
    public void TheNounIsWhatAPromptShouldCallIt(StoryLength length, string noun)
    {
        Assert.Equal(noun, ManuscriptLength.Noun(length));
    }

    [Fact]
    public void TheTextOverloadCountsWordsRatherThanTrustingACallerSCount()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 50_000));

        Assert.Equal(StoryLength.Novel, ManuscriptLength.Classify(text));
    }
}
