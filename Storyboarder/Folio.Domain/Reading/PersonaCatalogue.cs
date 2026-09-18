namespace Folio.Domain.Reading;

/// <summary>
/// The fifteen voices the report is read in: eight whole-book sections and seven readers.
///
/// <para>They live in one list because the thing that matters about them is collective — <b>no two
/// share a persona</b>. Two sections read by the same voice produce two paraphrases of one note,
/// which is the failure mode the whole per-persona design exists to remove, and it is invisible in
/// the output: the report still prints, it just says the same thing twice in different words.
/// <c>PersonaCoverageTests</c> is what fails instead.</para>
///
/// <para>A section with nothing to say prints nothing and its pass is skipped. A <i>reader</i> with
/// nothing to say is a failed pass, not a shorter one — a persona that only speaks when it is
/// negative is a different instrument, and the author would learn to skip it.</para>
/// </summary>
public static class PersonaCatalogue
{
    /// <summary>The eight whole-book sections, in the order the report prints them.</summary>
    public static IReadOnlyList<Persona> Sections { get; } =
    [
        new("story-integrity", "Story integrity",
            "Where does this book contradict itself, leave a hole, or get a fact wrong?",
            PersonaKind.Section, ModelTier.Standard),
        new("structure", "Structure",
            "Does the spine hold: does each act do its work, and in the right order?",
            PersonaKind.Section, ModelTier.Standard),
        new("setup-and-payoff", "Setup and payoff",
            "What is promised and never paid, and what arrives without having been set up?",
            PersonaKind.Section, ModelTier.Standard),
        new("theme-hierarchy", "Theme hierarchy",
            "What is this book about, in order, and where does a lesser theme crowd the first?",
            PersonaKind.Section, ModelTier.Standard),
        new("pacing", "Pacing",
            "Chapter by chapter, where does the tension slacken and where does it carry?",
            PersonaKind.Section, ModelTier.Standard),
        new("voice-consistency", "Voice consistency",
            "Where does the prose drift from the book's own dominant voice?",
            PersonaKind.Section, ModelTier.Standard),
        new("series-continuity", "Series continuity",
            "Where does this book contradict the canon of the one before it?",
            PersonaKind.Section, ModelTier.Standard),
        new("length", "Length",
            "How does this draft's length sit against its genre, and where is the slack or the gap?",
            PersonaKind.Section, ModelTier.Standard),
    ];

    /// <summary>The seven readers, in the order the report prints them, each on a page of its own.</summary>
    public static IReadOnlyList<Persona> Readers { get; } =
    [
        new("developmental-editor", "Developmental editor", "Structure",
            PersonaKind.Reader, ModelTier.Strong),
        new("line-editor", "Line editor", "Sentence by sentence",
            PersonaKind.Reader, ModelTier.Strong),
        new("agent", "Agent", "Could I sell it",
            PersonaKind.Reader, ModelTier.Strong),
        new("acquiring-editor", "Acquiring editor", "Would I buy it",
            PersonaKind.Reader, ModelTier.Strong),
        new("genre-fan", "Genre fan", "Would I finish it",
            PersonaKind.Reader, ModelTier.Strong),
        new("harsh-critic", "Harsh but fair critic", "What is wrong with it",
            PersonaKind.Reader, ModelTier.Strong),
        new("literature-professor", "Graduate literature professor", "What is it doing",
            PersonaKind.Reader, ModelTier.Strong),
    ];

    public static IReadOnlyList<Persona> All { get; } = [.. Sections, .. Readers];

    public static Persona ById(string id) =>
        All.FirstOrDefault(persona => persona.Id == id)
        ?? throw new KeyNotFoundException(
            $"No persona '{id}'. A read attributed to a persona that does not exist would print a "
            + "provenance line naming nobody, which is worse than printing none.");
}
