using Folio.Domain.Reading;
using Folio.Domain.Text;

namespace Folio.Tests.Reading;

/// <summary>
/// The cached prefix must not depend on which persona is reading.
///
/// <para>The bug this reintroduces is naming the persona ahead of the manuscript — and it was
/// reintroduced by accident, in the harness that measured the passes, which is how it was found.
/// Prompt caching matches an exact prefix, so a persona in front of the manuscript makes each of
/// the fourteen passes a different prefix: measured at <c>cache_creation: 167187</c> and
/// <c>cache_read: 0</c> on every pass, roughly $17 a draft on the strong model against $2.</para>
///
/// <para>Nothing about it is visible. Every request succeeds, every reply is right, every other test
/// passes. The bill is the only symptom, and nobody reads the bill until it is a quarter's worth.</para>
/// </summary>
public class ReadingPromptTests
{
    private const string Manuscript = "Chapter one. The tide was out and the wall was down.";

    [Fact]
    public void TheManuscriptIsTheFirstBlockAndCarriesTheCacheBreakpoint()
    {
        var blocks = ReadingPrompt.System(Manuscript, PersonaCatalogue.ById("pacing"), GenreFrame.Unknown);

        Assert.True(blocks[0].Cached, "The first block is not the cached one, so nothing before it is shared.");
        Assert.Contains(Manuscript, blocks[0].Text, StringComparison.Ordinal);
    }

    /// <summary>The invariant itself, over every pair of the fourteen personas.</summary>
    [Fact]
    public void EveryPersonaSharesByteIdenticalCachedPrefix()
    {
        var prefixes = PersonaCatalogue.All
            .Select(persona => string.Concat(
                ReadingPrompt.System(Manuscript, persona, GenreFrame.Unknown)
                    .TakeWhile(block => block.Cached).Select(block => block.Text)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(prefixes.Count == 1,
            $"{prefixes.Count} different cached prefixes across {PersonaCatalogue.All.Count} personas — "
            + "the manuscript will be written to the cache once per pass instead of once per read.");
    }

    [Fact]
    public void NothingPersonaSpecificAppearsBeforeTheBreakpoint()
    {
        foreach (var persona in PersonaCatalogue.All)
        {
            var cached = string.Concat(ReadingPrompt.System(Manuscript, persona, GenreFrame.Unknown)
                .TakeWhile(block => block.Cached).Select(block => block.Text));

            Assert.DoesNotContain(persona.Name, cached, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(persona.Id, cached, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(persona.Question, cached, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheInstructionAfterTheBreakpointDoesNameThePersona()
    {
        var persona = PersonaCatalogue.ById("genre-fan");
        var blocks = ReadingPrompt.System(Manuscript, persona, GenreFrame.Unknown);

        Assert.Contains(persona.Name, blocks[^1].Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(blocks[^1].Cached);
    }

    /// <summary>
    /// A reader is told nobody else's notes are available, because the report's claim about its own
    /// readers — "none of them saw the others' notes" — is only true if the prompt makes it true.
    /// </summary>
    [Fact]
    public void AReaderIsToldItIsReadingAlone()
    {
        var instruction = ReadingPrompt.Instruction(PersonaCatalogue.ById("agent"), NovelWords, GenreFrame.Unknown);

        Assert.Contains("Nobody else's notes", instruction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASectionIsToldToLeaveTheRestAlone()
    {
        var instruction = ReadingPrompt.Instruction(PersonaCatalogue.ById("pacing"), NovelWords, GenreFrame.Unknown);

        Assert.Contains("Leave every other concern", instruction, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The bug a real device run surfaced: a 1,209-word, one-chapter short story was told, in its own
    /// system prompt, that it was "the complete novel manuscript" — which is how a Themes section and a
    /// chapter-by-chapter Pacing curve came back for a book with neither.
    /// </summary>
    [Fact]
    public void AShortManuscriptIsCalledAShortStoryNotANovel()
    {
        const string shortManuscript = "A short tale. The hero walked to the castle and knocked on the gate.";
        var blocks = ReadingPrompt.System(shortManuscript, PersonaCatalogue.ById("pacing"), GenreFrame.Unknown);

        Assert.Contains("short story manuscript", blocks[^1].Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("novel manuscript", blocks[^1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ANovelLengthManuscriptIsStillCalledANovel()
    {
        var novelManuscript = string.Join(' ', Enumerable.Repeat("word", 90_000));
        var blocks = ReadingPrompt.System(novelManuscript, PersonaCatalogue.ById("pacing"), GenreFrame.Unknown);

        Assert.Contains("novel manuscript", blocks[^1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ShortStoryWords, "short story")]
    [InlineData(20_000, "novella")]
    [InlineData(NovelWords, "novel")]
    public void EveryPersonaKindNamesTheActualLength(int wordCount, string noun)
    {
        foreach (var persona in PersonaCatalogue.All)
        {
            var instruction = ReadingPrompt.Instruction(persona, wordCount, GenreFrame.Unknown);
            Assert.Contains($"{noun} manuscript", instruction, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Every pass is told the count itself, not only the noun it implies, so a reader judging length
    /// judges this manuscript's length rather than guessing at it.
    /// </summary>
    [Fact]
    public void EveryPersonaIsToldTheWordCount()
    {
        var manuscript = string.Join(' ', Enumerable.Repeat("word", ShortStoryWords));

        foreach (var persona in PersonaCatalogue.All)
        {
            var instruction = ReadingPrompt.System(manuscript, persona, GenreFrame.Unknown)[^1].Text;
            Assert.Contains("1,209 words", instruction, StringComparison.Ordinal);
        }
    }

    /// <summary>The count sits after the breakpoint: telling it must not cost the cache.</summary>
    [Fact]
    public void TheWordCountIsNotInTheCachedPrefix()
    {
        var manuscript = string.Join(' ', Enumerable.Repeat("word", ShortStoryWords));

        Assert.DoesNotContain("1,209", ReadingPrompt.CachedPrefix(manuscript), StringComparison.Ordinal);
    }

    /// <summary>
    /// A short story is read as one. Told only its noun, a reader still went looking for chapters,
    /// subplots and a theme hierarchy, and counted the story's length against it.
    /// </summary>
    [Fact]
    public void AShortStoryIsNotReadForANovel()
    {
        foreach (var persona in PersonaCatalogue.All)
        {
            var instruction = ReadingPrompt.Instruction(persona, ShortStoryWords, GenreFrame.Unknown);
            Assert.Contains("Read it as a short story", instruction, StringComparison.Ordinal);
            Assert.Contains("do not count its length", instruction, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ANovelIsNotToldItIsAShortStory()
    {
        foreach (var persona in PersonaCatalogue.All)
        {
            Assert.DoesNotContain("short story", ReadingPrompt.Instruction(persona, NovelWords, GenreFrame.Unknown),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The owner's ruling: on a short story the Agent's "I can't sell a short story" is noise the
    /// author already knows. It reads instead for the author's talent and its marketability, and
    /// recommends a path to magazine publication, outside an agent's usual purview or not.
    /// </summary>
    [Fact]
    public void TheAgentReadsAShortStoryForTalentAndAMagazinePath()
    {
        var agent = PersonaCatalogue.ById("agent");
        var instruction = ReadingPrompt.Instruction(agent, ShortStoryWords, GenreFrame.Unknown);

        Assert.Contains("do not say so", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("talent", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("magazine publication", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(agent.Question, ReadingPrompt.Question(agent, StoryLength.ShortStory));
    }

    [Fact]
    public void TheAgentReadsANovelAsBefore()
    {
        var agent = PersonaCatalogue.ById("agent");

        Assert.DoesNotContain("magazine", ReadingPrompt.Instruction(agent, NovelWords, GenreFrame.Unknown), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(agent.Question, ReadingPrompt.Question(agent, StoryLength.Novel));
        Assert.Equal(agent.Question, ReadingPrompt.Question(agent, StoryLength.Novella));
    }

    /// <summary>The magazine brief is the Agent's alone; five readers recommending markets is one note five times.</summary>
    [Fact]
    public void NoOtherPersonaIsGivenTheMagazineBrief()
    {
        foreach (var persona in PersonaCatalogue.All.Where(persona => persona.Id != "agent"))
        {
            Assert.DoesNotContain("magazine", ReadingPrompt.Instruction(persona, ShortStoryWords, GenreFrame.Unknown),
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(persona.Question, ReadingPrompt.Question(persona, StoryLength.ShortStory));
        }
    }

    /// <summary>
    /// Every pass is told the genre read from the text and the length its market expects: genre
    /// decides whether a draft is long or short, and so whether it needs trimming, expanding, or
    /// splitting into volumes.
    /// </summary>
    [Fact]
    public void EveryPersonaIsToldTheGenreAndItsUsualLength()
    {
        var novel = string.Join(' ', Enumerable.Repeat("word", NovelWords));
        foreach (var persona in PersonaCatalogue.All)
        {
            var instruction = ReadingPrompt.System(novel, persona, CozyMystery)[^1].Text;
            Assert.Contains("Cozy mystery", instruction, StringComparison.Ordinal);
            Assert.Contains("70,000-90,000", instruction, StringComparison.Ordinal);
        }
    }

    /// <summary>A genre's usual range is a book's length; a short story is not measured against it.</summary>
    [Fact]
    public void AShortStoryIsToldItsGenreButNotTheNovelRange()
    {
        var instruction = ReadingPrompt.Instruction(PersonaCatalogue.ById("length"), ShortStoryWords, CozyMystery);

        Assert.Contains("Cozy mystery", instruction, StringComparison.Ordinal);
        Assert.DoesNotContain("70,000-90,000", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void AReaderIsNamedWithTheRightArticle()
    {
        Assert.Contains("You are an Agent", ReadingPrompt.Instruction(PersonaCatalogue.ById("agent"), NovelWords, GenreFrame.Unknown), StringComparison.Ordinal);
        Assert.Contains("You are a Genre fan", ReadingPrompt.Instruction(PersonaCatalogue.ById("genre-fan"), NovelWords, GenreFrame.Unknown), StringComparison.Ordinal);
    }

    [Fact]
    public void TheGenreIsNotInTheCachedPrefix()
    {
        foreach (var persona in PersonaCatalogue.All)
        {
            var cached = string.Concat(ReadingPrompt.System(Manuscript, persona, CozyMystery)
                .TakeWhile(block => block.Cached).Select(block => block.Text));
            Assert.Equal(ReadingPrompt.CachedPrefix(Manuscript), cached);
        }
    }

    [Fact]
    public void AnUnknownGenreIsNotNamed()
    {
        foreach (var persona in PersonaCatalogue.All)
        {
            Assert.DoesNotContain("its genre", ReadingPrompt.Instruction(persona, NovelWords, GenreFrame.Unknown),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The owner's intent: the genre fan is a fan of THIS book's genre, predisposed to like it and
    /// particular about how it is done because they have read so much of it. Called only "Genre fan",
    /// the model read the word the way the trade uses it and became a reader of science fiction,
    /// fantasy and horror whatever the book was.
    /// </summary>
    [Fact]
    public void TheGenreFanIsAFanOfThisBooksGenre()
    {
        var instruction = ReadingPrompt.Instruction(PersonaCatalogue.ById("genre-fan"), NovelWords, CozyMystery);

        Assert.Contains("devoted reader of Cozy mystery", instruction, StringComparison.Ordinal);
        Assert.Contains("want to like it", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("particular", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not science fiction, fantasy or horror", instruction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheGenreFanWithNoGenreStillReadsThisBooksOwnGenre()
    {
        var instruction = ReadingPrompt.Instruction(PersonaCatalogue.ById("genre-fan"), NovelWords, GenreFrame.Unknown);

        Assert.Contains("this book's own genre", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not science fiction, fantasy or horror", instruction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnlyTheGenreFanIsMadeAFan()
    {
        foreach (var persona in PersonaCatalogue.All.Where(persona => persona.Id != "genre-fan"))
        {
            Assert.DoesNotContain("devoted reader", ReadingPrompt.Instruction(persona, NovelWords, CozyMystery),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The length section weighs the story against the length, both ways: too much plot for the
    /// words (trim, or split into volumes) and too little to carry them (more needs to happen).
    /// </summary>
    [Fact]
    public void TheLengthSectionWeighsThePlotAgainstTheLength()
    {
        var question = PersonaCatalogue.ById("length").Question;

        Assert.Contains("genre", question, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("volumes", question, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("more needs to happen", question, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly GenreFrame CozyMystery = new("Cozy mystery", "70,000-90,000");
    private const int ShortStoryWords = 1_209;
    private const int NovelWords = 90_000;
}
