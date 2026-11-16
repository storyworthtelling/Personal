namespace Folio.Domain.Reading;

/// <summary>
/// The fourteen voices the report is read in: eight whole-book sections and six readers.
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
            "Against its genre's usual length, is this draft the right size for its story: too much plot "
            + "for the words (trim it, or split it into volumes), too little to carry them (more needs to "
            + "happen), and where is the slack or the gap?",
            PersonaKind.Section, ModelTier.Standard),
    ];

    /// <summary>
    /// The six readers, in the order the report prints them, each on a page of its own.
    ///
    /// <para>A seventh, Line editor, stood here through Release F and G. The owner dropped it on the
    /// first report read from a real device: its whole subject — sentence-by-sentence craft, quoted
    /// against the line — is exactly what <c>LineEdit</c> already puts on the manuscript itself as
    /// Query marks (<see cref="Domain.Markup.MarkSource"/>), so a reader built to say the same thing
    /// in character was reading the marks back as prose. The other six ask a question the marked
    /// manuscript cannot answer — sellability, structure, whether a stranger finishes it — which is
    /// what a whole-book voice is for.</para>
    /// </summary>
    public static IReadOnlyList<Persona> Readers { get; } =
    [
        // Each reader's anchors are its own scale, in its own terms (the owner's ruling): a 2, 4, 6, 8
        // and 10 written down so the number means the same from one read to the next.
        new("developmental-editor", "Developmental editor", "Structure",
            PersonaKind.Reader, ModelTier.Strong)
        {
            ScoreAnchors =
            [
                "the structure does not hold; it needs rebuilding from the outline",
                "the bones are there, but it needs major structural work: parts reordered, arcs rebuilt",
                "sound structure with real soft spots; a substantive revision pass",
                "structurally solid; only targeted fixes",
                "there is nothing structural I would touch",
            ],
        },
        // The owner's ruling: on a short story "I can't sell a short story" is the whole of what an
        // agent in character says, and the author already knows it. So on a short story the Agent
        // reads for the talent and recommends a path to magazines, outside its usual purview or not.
        new("agent", "Agent", "Could I sell it",
            PersonaKind.Reader, ModelTier.Strong)
        {
            ShortStoryQuestion = "What does it show of the writer, and where should it go",
            ShortStoryBrief =
                "The author knows an agent does not take on a single short story: do not say so, and do not "
                + "judge the story on whether you could sell it to a publisher. Read it instead for what it "
                + "shows of the author's talent and how marketable that talent is: the voice, the craft, what "
                + "would make you ask to see more. Then, even though it is outside an agent's usual work, "
                + "recommend a path toward magazine publication: the kinds of magazines and markets this story "
                + "suits, by genre and tier, naming only markets you are confident exist and noting that their "
                + "guidelines must be checked; whether it is ready to submit and, if not, what to do first; and "
                + "the order to submit in. The talent goes in your verdict; the path goes in your review.",
            ScoreAnchors =
            [
                "I would pass on the query",
                "I would pass with a personal note; the writer is not there yet",
                "I would request the full manuscript, and probably pass after reading it",
                "I would offer representation, with revisions before it goes out",
                "I would offer today and expect more than one publisher to bid",
            ],
            ShortStoryScoreAnchors =
            [
                "nothing here yet that I would ask to see more of",
                "flashes of ability, but the craft is not yet professional",
                "a competent, professional writer without a distinctive voice yet",
                "a distinctive voice I would want to follow; I would ask what else they are writing",
                "a writer I would chase now, with a story ready for the top magazines",
            ],
        },
        new("acquiring-editor", "Acquiring editor", "Would I buy it",
            PersonaKind.Reader, ModelTier.Strong)
        {
            ScoreAnchors =
            [
                "I would not take it to the acquisitions meeting",
                "I would decline, but I would look at this author's next book",
                "I would take it to acquisitions only with heavy revision conditions",
                "I would fight for it at acquisitions, with an ordinary editorial pass",
                "I would pre-empt to keep it from going to auction",
            ],
        },
        new("genre-fan", "Genre fan", "Would I finish it",
            PersonaKind.Reader, ModelTier.Strong)
        {
            FanOfTheGenre = true,
            ScoreAnchors =
            [
                "I would put it down in the opening pages",
                "I would push through out of loyalty to the genre, and not recommend it",
                "I would finish it, satisfied but not excited",
                "I would finish it fast and press it on other fans",
                "an instant favourite; I would buy this author's next on release day",
            ],
        },
        new("harsh-critic", "Harsh but fair critic", "What is wrong with it",
            PersonaKind.Reader, ModelTier.Strong)
        {
            // A higher score is less wrong, so a 10 here means the same direction as everyone else's.
            ScoreAnchors =
            [
                "fundamentally broken, with problems on every page",
                "serious flaws that undermine what it does well",
                "real flaws a careful reader will notice, but it works",
                "minor flaws I had to go looking for",
                "I tried to find fault and found nothing worth printing",
            ],
        },
        new("literature-professor", "Graduate literature professor", "What is it doing",
            PersonaKind.Reader, ModelTier.Strong)
        {
            ScoreAnchors =
            [
                "it does nothing beyond telling its story; there is nothing to discuss",
                "it gestures at ideas it does not develop",
                "coherent intentions, carried out unevenly",
                "it does something deliberate and does it well; it would reward a seminar",
                "it does something with the form that I would teach",
            ],
        },
    ];

    public static IReadOnlyList<Persona> All { get; } = [.. Sections, .. Readers];

    public static Persona ById(string id) =>
        All.FirstOrDefault(persona => persona.Id == id)
        ?? throw new KeyNotFoundException(
            $"No persona '{id}'. A read attributed to a persona that does not exist would print a "
            + "provenance line naming nobody, which is worse than printing none.");
}
