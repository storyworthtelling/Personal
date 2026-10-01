using Folio.Domain.Reading;
using Folio.Domain.Text;

namespace Folio.Tests.Reading;

/// <summary>
/// The cached prefix must not depend on which persona is reading.
///
/// <para>The bug this reintroduces is naming the persona ahead of the manuscript — and it was
/// reintroduced by accident, in the harness that measured the passes, which is how it was found.
/// Prompt caching matches an exact prefix, so a persona in front of the manuscript makes each of
/// the seventeen passes a different prefix: measured at <c>cache_creation: 167187</c> and
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
        var blocks = ReadingPrompt.System(Manuscript, PersonaCatalogue.ById("pacing"));

        Assert.True(blocks[0].Cached, "The first block is not the cached one, so nothing before it is shared.");
        Assert.Contains(Manuscript, blocks[0].Text, StringComparison.Ordinal);
    }

    /// <summary>The invariant itself, over every pair of the fifteen personas.</summary>
    [Fact]
    public void EveryPersonaSharesByteIdenticalCachedPrefix()
    {
        var prefixes = PersonaCatalogue.All
            .Select(persona => string.Concat(
                ReadingPrompt.System(Manuscript, persona)
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
            var cached = string.Concat(ReadingPrompt.System(Manuscript, persona)
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
        var blocks = ReadingPrompt.System(Manuscript, persona);

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
        var instruction = ReadingPrompt.Instruction(PersonaCatalogue.ById("agent"), StoryLength.Novel);

        Assert.Contains("Nobody else's notes", instruction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASectionIsToldToLeaveTheRestAlone()
    {
        var instruction = ReadingPrompt.Instruction(PersonaCatalogue.ById("pacing"), StoryLength.Novel);

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
        var blocks = ReadingPrompt.System(shortManuscript, PersonaCatalogue.ById("pacing"));

        Assert.Contains("short story manuscript", blocks[^1].Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("novel manuscript", blocks[^1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ANovelLengthManuscriptIsStillCalledANovel()
    {
        var novelManuscript = string.Join(' ', Enumerable.Repeat("word", 90_000));
        var blocks = ReadingPrompt.System(novelManuscript, PersonaCatalogue.ById("pacing"));

        Assert.Contains("novel manuscript", blocks[^1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(StoryLength.ShortStory, "short story")]
    [InlineData(StoryLength.Novella, "novella")]
    [InlineData(StoryLength.Novel, "novel")]
    public void EveryPersonaKindNamesTheActualLength(StoryLength length, string noun)
    {
        foreach (var persona in PersonaCatalogue.All)
        {
            var instruction = ReadingPrompt.Instruction(persona, length);
            Assert.Contains($"{noun} manuscript", instruction, StringComparison.OrdinalIgnoreCase);
        }
    }
}
