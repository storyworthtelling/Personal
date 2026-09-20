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
    public void TheSixReadersEachHaveOne()
    {
        Assert.Equal(6, PersonaCatalogue.Readers.Count);
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
    public void TheReadersAreTheApprovedSixInTheApprovedOrder()
    {
        Assert.Equal(
            new[]
            {
                "Developmental editor", "Agent", "Acquiring editor", "Genre fan",
                "Harsh but fair critic", "Graduate literature professor",
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
}
