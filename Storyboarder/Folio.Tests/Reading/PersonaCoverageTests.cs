using Folio.Domain.Reading;

namespace Folio.Tests.Reading;

/// <summary>
/// G8 — every section and every one of the six readers has a persona, and no two share one.
///
/// <para>The bug this reintroduces is dropping a persona so two sections share one. It produces a
/// report that renders perfectly and says the same thing twice in different words, which is exactly
/// the failure the per-persona design exists to remove and exactly the failure no other test can
/// see: repetition is not an error, it is prose.</para>
/// </summary>
public class PersonaCoverageTests
{
    [Fact]
    public void TheEightWholeBookSectionsEachHaveOne()
    {
        Assert.Equal(8, PersonaCatalogue.Sections.Count);
        Assert.All(PersonaCatalogue.Sections, persona => Assert.Equal(PersonaKind.Section, persona.Kind));
    }

    [Fact]
    public void TheSevenReadersEachHaveOne()
    {
        Assert.Equal(7, PersonaCatalogue.Readers.Count);
        Assert.All(PersonaCatalogue.Readers, persona => Assert.Equal(PersonaKind.Reader, persona.Kind));
    }

    [Fact]
    public void NoTwoShareAPersona()
    {
        var ids = PersonaCatalogue.All.Select(persona => persona.Id).ToList();
        var names = PersonaCatalogue.All.Select(persona => persona.Name).ToList();
        var questions = PersonaCatalogue.All.Select(persona => persona.Question).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(questions.Count, questions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// The readers by name and order. Six now, not the drawn seven — the owner dropped Line editor
    /// after the first real report, since its whole subject already prints as marks on the
    /// manuscript. The Gate-3 mockup still shows seven; this is the amendment, not a drift from it.
    /// </summary>
    [Fact]
    public void TheReadersAreTheApprovedSevenInTheApprovedOrder()
    {
        Assert.Equal(
            new[]
            {
                "Developmental editor", "Agent", "Acquiring editor", "Genre fan",
                "Harsh but fair critic", "Graduate literature professor", "Fellow writer",
            },
            PersonaCatalogue.Readers.Select(persona => persona.Name).ToArray());
    }

    [Fact]
    public void TheSectionsAreTheDrawnEightInTheDrawnOrder()
    {
        Assert.Equal(
            new[]
            {
                "Story integrity", "Structure", "Setup and payoff", "Theme hierarchy", "Pacing",
                "Voice consistency", "Series continuity", "Length",
            },
            PersonaCatalogue.Sections.Select(persona => persona.Name).ToArray());
    }

    /// <summary>
    /// The model mix, as reversal 2 settled it: judgement to the strong model, structure to the
    /// standard one. A section quietly promoted to the strong tier is roughly a threefold cost rise
    /// on eight whole-manuscript passes and nothing in the output says so.
    /// </summary>
    [Fact]
    public void JudgementGoesToTheStrongModelAndStructureToTheStandardOne()
    {
        Assert.All(PersonaCatalogue.Readers, persona => Assert.Equal(ModelTier.Strong, persona.Tier));
        Assert.All(PersonaCatalogue.Sections, persona => Assert.Equal(ModelTier.Standard, persona.Tier));
    }

    [Fact]
    public void EveryPersonaCarriesAQuestionItIsAskedToAnswer()
    {
        Assert.All(PersonaCatalogue.All, persona =>
        {
            Assert.False(string.IsNullOrWhiteSpace(persona.Question),
                $"{persona.Name} has no question, so its pass has nothing to ask.");
            Assert.False(string.IsNullOrWhiteSpace(persona.Name));
            Assert.False(string.IsNullOrWhiteSpace(persona.Id));
        });
    }

    [Fact]
    public void AReadAttributedToNobodyIsRefusedRatherThanPrinted()
    {
        Assert.Throws<KeyNotFoundException>(() => PersonaCatalogue.ById("continuity-elf"));
    }

    /// <summary>
    /// The approved cast (R42, owner's rulings): each reader has an invented punny name and a voice of its
    /// own, no two alike. The names are wire-visible on every reader page, so a change is a decision.
    /// </summary>
    [Fact]
    public void EveryReaderHasItsApprovedNameAndAVoiceOfItsOwn()
    {
        Assert.Equal(
            ["Archie Tecture", "Hugo Hook", "Edie Torial", "Rhea Dalot", "Nick Picker", "Prof. Craft", "Ina Kwell"],
            PersonaCatalogue.Readers.Select(persona => persona.ReaderName));
        Assert.All(PersonaCatalogue.Readers, persona => Assert.False(string.IsNullOrWhiteSpace(persona.Voice)));
        Assert.Equal(7, PersonaCatalogue.Readers.Select(persona => persona.Voice).Distinct().Count());
    }

    [Fact]
    public void SectionsHaveNoNameAndNoVoice() =>
        Assert.All(PersonaCatalogue.Sections, persona =>
        {
            Assert.Null(persona.ReaderName);
            Assert.Null(persona.Voice);
        });

    /// <summary>The seventh reader (R10, R34): the Fellow writer, scoring on what they'd steal.</summary>
    [Fact]
    public void TheFellowWriterReadsForWhatToStealAndWhatToDoDifferently()
    {
        var writer = PersonaCatalogue.ById("fellow-writer");

        Assert.Equal("What would I steal, and what would I do differently", writer.Question);
        Assert.Equal(ModelTier.Strong, writer.Tier);
        Assert.Equal("I wish I'd written it; I'd steal everything and change nothing", writer.ScoreAnchors[^1]);
    }

    /// <summary>The Acquiring editor writes the edit letter (R35): its question and its approved anchors.</summary>
    [Fact]
    public void TheAcquiringEditorAsksWhatWouldMostRaiseIt()
    {
        var editor = PersonaCatalogue.ById("acquiring-editor");

        Assert.Equal("What would most raise it", editor.Question);
        Assert.Equal(
            ["it needs rebuilding before an edit letter could help",
             "the letter would be long; the problems are structural",
             "three substantive changes would lift it a level",
             "a light letter; the changes are refinements",
             "I'd have nothing to ask for"],
            editor.ScoreAnchors);
    }

    /// <summary>The Agent keeps "Could I sell it" (owner's ruling); its craft focus is in the brief, not the question.</summary>
    [Fact]
    public void TheAgentStillAsksCouldISellIt() =>
        Assert.Equal("Could I sell it", PersonaCatalogue.ById("agent").Question);
}
