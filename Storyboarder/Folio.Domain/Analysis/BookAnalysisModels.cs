using System.Text.Json;
using System.Text.Json.Serialization;

namespace Folio.Domain.Analysis;

// The per-chunk literary observations (the "map" output for the revision report). Everything is a
// flat list of short strings so one chunk's notes stay compact enough to fold many together in the
// reduce step. The client maps missing fields to empty lists, so a partial reply degrades gracefully.
//
// Chapter is the label of the chapter(s) the chunk covers ("Chapter 7", "Chapters 7–8"), stamped on
// by the client rather than asked of the model: it is known exactly from the chunk's offsets, and it
// is what lets every finding in the consolidated report cite a place in the book. Empty for a
// conversion chunked before chapter detection existed.
//
// The last four lists are the craft half of the read — the raw material the reduce step turns into
// the line-edit, prose/voice, dialogue and scene sections. They are gathered per chunk because they
// need the actual sentences in front of the model; by consolidation time only the notes remain.
public sealed record ChapterNotes(
    string Chapter,
    IReadOnlyList<string> CharactersPresent,
    IReadOnlyList<string> KeyEvents,
    IReadOnlyList<string> CharacterBeats,
    IReadOnlyList<string> SetupsAndPayoffs,
    IReadOnlyList<string> PossibleInconsistencies,
    IReadOnlyList<string> FactualClaims,
    IReadOnlyList<string> NotableStrengths,
    IReadOnlyList<string> NotableWeaknesses,
    IReadOnlyList<string> LineEditCandidates,
    IReadOnlyList<string> ProseObservations,
    IReadOnlyList<string> DialogueObservations,
    IReadOnlyList<string> SceneBeats)
{
    // A cumulative "story so far" the map call carries chapter-to-chapter: the running plot state, open
    // threads, and planted-but-unpaid setups as of the end of this chunk, built from the prior chunk's state
    // plus this chunk. It gives the consolidation (and cross-draft reconciliation) long-range continuity for
    // setup/payoff and for contradictions a fix in one chapter creates in a distant one. Init-only with an
    // empty default so callers that don't thread it (and reports stored before it existed) are unaffected.
    public string RunningState { get; init; } = string.Empty;

    // The raw material for the expansion engine (Phase 3), gathered here while the actual prose is in front
    // of the model: SummarizedMoments = told-not-shown passages that could become scenes (the show-vs-tell
    // lever); ThinSpots = rushed/over-compressed beats ("runs rushed"); OffPageEvents = turns that happen
    // off-page (candidate missing beats). Init-only/empty so older notes and callers are unaffected.
    public IReadOnlyList<string> SummarizedMoments { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ThinSpots { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OffPageEvents { get; init; } = Array.Empty<string>();

    public static ChapterNotes Empty { get; } = new(
        string.Empty,
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
}

// One observation about the book, with the chapter it was drawn from. The author's first question of
// any finding is "where?", so the citation is part of the finding rather than something the model is
// asked to work into the prose of Detail.
//
// Serialized as {"chapter":…,"detail":…}, but a bare JSON string is also accepted: that is the shape
// every report consolidated before chapter citations existed was stored in, and re-rendering one of
// those must not throw — it simply has nothing to cite.
[JsonConverter(typeof(ReportFindingJsonConverter))]
public sealed record ReportFinding(string Chapter, string Detail)
{
    public static ReportFinding Of(string detail) => new(string.Empty, detail);

    /// <summary>
    /// A short passage quoted verbatim from the manuscript, when the finding is about one. It is what
    /// lets the finding be written on the page it concerns instead of listed in a section: with it,
    /// the note lands on the sentence; without it, on the opening of the chapter cited. Empty for a
    /// report stored before findings carried one, which is why it is init-only with an empty default.
    /// </summary>
    public string Excerpt { get; init; } = string.Empty;
}

public sealed class ReportFindingJsonConverter : JsonConverter<ReportFinding>
{
    private const string ChapterProperty = "chapter";
    private const string DetailProperty = "detail";
    private const string ExcerptProperty = "excerpt";

    public override ReportFinding Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return ReportFinding.Of(reader.GetString() ?? string.Empty);

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException(
                $"A report finding must be a JSON string or object, but was {reader.TokenType}.");

        var chapter = string.Empty;
        var detail = string.Empty;
        var excerpt = string.Empty;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return new ReportFinding(chapter, detail) { Excerpt = excerpt };
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var name = reader.GetString();
            reader.Read();
            var value = reader.TokenType == JsonTokenType.String ? reader.GetString() ?? string.Empty : null;
            if (value is null)
                reader.Skip();
            else if (string.Equals(name, ChapterProperty, StringComparison.OrdinalIgnoreCase))
                chapter = value;
            else if (string.Equals(name, DetailProperty, StringComparison.OrdinalIgnoreCase))
                detail = value;
            else if (string.Equals(name, ExcerptProperty, StringComparison.OrdinalIgnoreCase))
                excerpt = value;
        }

        throw new JsonException("A report finding object was not terminated.");
    }

    public override void Write(Utf8JsonWriter writer, ReportFinding value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(ChapterProperty, value.Chapter ?? string.Empty);
        writer.WriteString(DetailProperty, value.Detail ?? string.Empty);
        // Written only when there is one, so a finding with no quote serialises exactly as it always
        // did and nothing stored changes shape.
        if (!string.IsNullOrWhiteSpace(value.Excerpt))
            writer.WriteString(ExcerptProperty, value.Excerpt);
        writer.WriteEndObject();
    }
}

// A character's place in the story: role, arc, and key relationships (rendered as the character map).
public sealed record CharacterProfile(
    string Name,
    string Role,
    string ArcSummary,
    IReadOnlyList<string> Relationships);

/// <summary>
/// One reader's whole-book read, in character. Seven of these, one to a printed page.
///
/// <para><b>Every part is load-bearing and none of them may arrive empty</b> (F44, F46). The
/// <see cref="Verdict"/> is the clause the readers' contents page carries; the
/// <see cref="Assessment"/> is the long review; the two lists are that reader's own pros and cons —
/// the one place in the report where a tick and a cross are the right marks, because these are one
/// reader's preferences rather than the book's severity tiers; and the <see cref="Condition"/> is
/// the "Would need" line.</para>
///
/// <para><c>Recommendation</c> is what <see cref="Condition"/> used to be called. The rename is not
/// cosmetic: printed where the author reads it, the field is a <i>condition on the verdict</i> — what
/// would have to change before this reader changed their mind — and "recommendation" invited a
/// restatement of the review instead.</para>
///
/// <para><see cref="PersonaId"/> is a key into <c>PersonaCatalogue</c> rather than free text,
/// because the to-do list cites the persona that raised each item and a citation naming a
/// perspective nobody can look up is not a citation.</para>
/// </summary>
public sealed record Review(
    string PersonaId,
    string Verdict,
    string Assessment,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Weaknesses,
    string Condition);

// One actionable revision suggestion. The report lists these highest-impact-first.
public sealed record RevisionItem(
    string Priority,
    string Chapter,
    string Issue,
    string SuggestedFix);

// What is actually wrong with a line edit's sentence — so the manuscript's margin note can say
// something specific rather than the single catch-all "Unclear" every edit used to carry regardless
// of whether the problem was a filter word, a redundant clause, or a malformed sentence.
public enum LineEditKind
{
    Other,
    FilterWord,
    Redundant,
    WeakWord,
    Cliche,
    Grammar,
    Digression,
    Tone
}

// One sentence (or short passage) to cut, tighten, or rewrite, quoted so the author can find it.
public sealed record LineEdit(
    string Chapter,
    string Excerpt,
    string Suggestion,
    string Reason)
{
    // Defaults to Other for a stored report from before this existed, or a reply that omits it.
    public LineEditKind Kind { get; init; } = LineEditKind.Other;
}

// A prose-style or voice pattern: repetition, filter words ("she felt", "he saw"), a verbal tic,
// monotonous sentence rhythm, an overused construction, or a slip out of the established POV.
public sealed record ProseNote(
    string Chapter,
    string Pattern,
    string Evidence,
    string Fix);

// A dialogue critique: whether the speakers sound distinct, on-the-nose exchanges, and exposition
// dumped into speech.
public sealed record DialogueNote(
    string Chapter,
    string Speakers,
    string Observation,
    string Fix)
{
    // A verbatim quote of the actual exchange, when the note is about one — not every dialogue note
    // is: "all three sound identical" is an aggregate observation with nothing to point at. Empty
    // means exactly that, and MarkSource produces no mark for it.
    public string Excerpt { get; init; } = string.Empty;
}

// Whether a scene, once written, earns its place or does no work for the book.
public enum SceneVerdict
{
    EarnsItsPlace,
    DoesNoWork
}

// One scene in the scene-by-scene map: what happens, what it accomplishes for the book, and a
// verdict — which is what surfaces the scenes doing no work.
public sealed record SceneBeat(
    string Chapter,
    string Scene,
    string Purpose,
    string Verdict)
{
    // A verbatim quote from near the scene's own close, so its mark sits at the end of the scene it
    // is about rather than floating free of the text. Empty for an older stored report; MarkSource
    // produces no mark without something to anchor to.
    public string Excerpt { get; init; } = string.Empty;
    public SceneVerdict Outcome { get; init; } = SceneVerdict.EarnsItsPlace;
}

// The craft half of the report (line edits, prose/voice, dialogue, scene map). It is consolidated by
// its OWN Claude call: together with the story half it is far more output than one call can produce
// without either truncating or streaming long enough for the Android worker to be killed mid-flight.
public sealed record CraftReport(
    IReadOnlyList<LineEdit> LineEdits,
    IReadOnlyList<ProseNote> ProseNotes,
    IReadOnlyList<DialogueNote> DialogueNotes,
    IReadOnlyList<SceneBeat> SceneBreakdown)
{
    public static CraftReport Empty { get; } = new(
        Array.Empty<LineEdit>(), Array.Empty<ProseNote>(),
        Array.Empty<DialogueNote>(), Array.Empty<SceneBeat>());
}

// The whole consolidated revision report (the "reduce" output), rendered into the analysis PDF and
// aimed at helping the author revise toward publication. The craft sections arrive from a second
// call and are folded in with WithCraft, so the renderer only ever sees one report.
public sealed record BookAnalysisResult(
    string Logline,
    string Synopsis,
    IReadOnlyList<CharacterProfile> Characters,
    IReadOnlyList<string> Themes,
    IReadOnlyList<ReportFinding> PlotHoles,
    IReadOnlyList<ReportFinding> Inconsistencies,
    IReadOnlyList<ReportFinding> Inaccuracies,
    IReadOnlyList<ReportFinding> PacingNotes,
    IReadOnlyList<Review> Reviews,
    IReadOnlyList<RevisionItem> RevisionPlan,
    IReadOnlyList<string> ComparableTitles,
    string MarketPositioning,
    IReadOnlyList<LineEdit> LineEdits,
    IReadOnlyList<ProseNote> ProseNotes,
    IReadOnlyList<DialogueNote> DialogueNotes,
    IReadOnlyList<SceneBeat> SceneBreakdown)
{
    // Where the author's own prose drifts from the book's dominant authorial voice. Arrives from its own
    // call (it needs actual prose samples per chapter, not the notes the other reduces work from) and is
    // folded in the same way. Defaults to Empty so a report stored before this existed re-renders fine.
    public VoiceConsistency VoiceConsistency { get; init; } = VoiceConsistency.Empty;

    // The inferred genre and the word-count range publishers expect for it, and the manuscript's actual
    // word count, so the length read is framed against the genre norm. All default-empty for old reports.
    public string Genre { get; init; } = string.Empty;
    public string WordCountRange { get; init; } = string.Empty;
    public int WordCount { get; init; }

    // Tension per chapter (0 = calm, 1 = peak), rendered as a small chart so a saggy stretch is visible
    // at a glance.
    public IReadOnlyList<ChapterTension> PacingCurve { get; init; } = Array.Empty<ChapterTension>();

    // The candid state-of-the-draft that closes the report.
    public Verdict Verdict { get; init; } = Verdict.Empty;

    // The additive read (Phase 3): scenes/beats to ADD, the two-sided show-vs-tell notes, and the length
    // against the genre norm. Its own resumable call, folded in with WithExpansion like the craft/voice
    // halves. Defaults to Empty so a report stored before this existed re-renders fine.
    public ExpansionReport Expansion { get; init; } = ExpansionReport.Empty;

    // The cross-book continuity read (Phase 5): where this book contradicts the prior book's established canon
    // and how it handles the prior book's open threads. Its own resumable call, folded in with
    // WithSeriesContinuity. Empty for a standalone book (and for any report stored before this existed), so no
    // series section renders.
    public SeriesContinuity SeriesContinuity { get; init; } = SeriesContinuity.Empty;

    /// <summary>
    /// The eight whole-book section personas' own reads, carried through <b>whole</b> — not flattened
    /// into a string, which is what let their summaries overwrite <see cref="Themes"/> in the first
    /// place. The renderer routes each by <see cref="Reading.SectionRead.PersonaId"/>: three fold into
    /// a section that already exists for that ground (Pacing, Voice consistency, Series continuity),
    /// and the rest — Story integrity, Structure, Setup and payoff, Theme hierarchy, Length — get a
    /// section of their own. Themes keeps the model's own answer to what this book is about.
    /// </summary>
    public IReadOnlyList<Reading.SectionRead> SectionReads { get; init; } = [];

    public static BookAnalysisResult Empty { get; } = new(
        string.Empty, string.Empty, Array.Empty<CharacterProfile>(), Array.Empty<string>(),
        Array.Empty<ReportFinding>(), Array.Empty<ReportFinding>(), Array.Empty<ReportFinding>(), Array.Empty<ReportFinding>(),
        Array.Empty<Review>(), Array.Empty<RevisionItem>(), Array.Empty<string>(), string.Empty,
        Array.Empty<LineEdit>(), Array.Empty<ProseNote>(),
        Array.Empty<DialogueNote>(), Array.Empty<SceneBeat>());

    public BookAnalysisResult WithCraft(CraftReport craft) => this with
    {
        LineEdits = craft.LineEdits,
        ProseNotes = craft.ProseNotes,
        DialogueNotes = craft.DialogueNotes,
        SceneBreakdown = craft.SceneBreakdown
    };

    public BookAnalysisResult WithVoiceConsistency(VoiceConsistency voice) =>
        this with { VoiceConsistency = voice };

    public BookAnalysisResult WithWordCount(int wordCount) =>
        this with { WordCount = wordCount };

    public BookAnalysisResult WithExpansion(ExpansionReport expansion) =>
        this with { Expansion = expansion };

    public BookAnalysisResult WithSeriesContinuity(SeriesContinuity seriesContinuity) =>
        this with { SeriesContinuity = seriesContinuity };

    /// <summary>
    /// Folds the sixteen reads over the consolidated report.
    ///
    /// <para>The readers <b>replace</b> whatever the omnibus reduce produced rather than joining it:
    /// the whole point of a persona pass is that one voice read the whole book, and a page carrying
    /// both would be two different instruments under one name. Sections and synthesis only fill what
    /// they actually found, so a read that has not run yet, or ran and was silent, leaves the
    /// consolidated report's own answer standing rather than blanking it. Themes is never touched
    /// here — it is the model's own answer to what the book is about, and the sections are carried
    /// separately in <see cref="SectionReads"/> for the renderer to route.</para>
    /// </summary>
    public BookAnalysisResult WithReads(
        IReadOnlyList<Review> readers,
        IReadOnlyList<Reading.SectionRead> sections,
        IReadOnlyList<RevisionItem> revisionPlan,
        Verdict verdict) =>
        this with
        {
            Reviews = readers is { Count: > 0 } ? readers : Reviews,
            SectionReads = sections is { Count: > 0 } ? sections : SectionReads,
            RevisionPlan = revisionPlan is { Count: > 0 } ? revisionPlan : RevisionPlan,
            Verdict = string.IsNullOrWhiteSpace(verdict.MostNeeds)
                && verdict.Strengths is not { Count: > 0 }
                && verdict.Problems is not { Count: > 0 }
                    ? Verdict
                    : verdict,
        };
}

// The additive read — Folio's expansion engine (Phase 3). Because early drafts run short, this surfaces
// scenes and beats to ADD (never drafted prose — pointers only), the two-sided show-vs-tell notes (the main
// lever for adding words), and the manuscript's length against its genre norm. Its own Claude call and DB
// column, folded into the report at render like the craft/voice halves.
public sealed record ExpansionReport(
    IReadOnlyList<SceneAddition> ScenesToAdd,
    IReadOnlyList<ShowTellNote> ShowVsTell,
    LengthAssessment Length)
{
    public static ExpansionReport Empty { get; } =
        new(Array.Empty<SceneAddition>(), Array.Empty<ShowTellNote>(), LengthAssessment.Empty);
}

// The kind of scene a proposal would add. MissingBeat = a turn that happens off-page; CharacterDeepening = a
// scene that would deepen a character; TensionStakes = a scene that would raise tension/stakes;
// ThemeDeepening = a scene that would develop a thin theme. Persisted as a string in JSON (tolerant parse).
public enum AdditionKind
{
    MissingBeat,
    CharacterDeepening,
    TensionStakes,
    ThemeDeepening
}

// One nudge to ADD a scene — a pointer, never drafted prose or an example scene (the nudge-never-write
// principle). Location cites where it belongs ("Chapter 4", "after Ch. 7 · Scene 2"); Missing is what the
// reader never gets to see; Why is the impact; Priority reuses the Must/Should/Consider impact tier.
public sealed record SceneAddition(AdditionKind Kind, string Location, string Missing, string Why, string Priority)
{
    // A verbatim line from the passage the new scene belongs beside, so its caret can sit on the page.
    // Empty when the model named only a chapter, which places the caret at the chapter's head or close.
    public string Excerpt { get; init; } = string.Empty;
}

// Which end of the show-vs-tell pendulum a note is on: Dramatize a summarized passage that could become a
// shown scene (the additive lever), or Tighten an over-shown passage.
public enum ShowTellDirection
{
    Dramatize,
    Tighten
}

// One show-vs-tell note, both ends of the pendulum. Chapter locates it; Excerpt quotes the prose verbatim so
// the author can find it; Suggestion is a pointer ("this summary could be a scene" / "this runs long"), never
// a rewrite.
public sealed record ShowTellNote(string Chapter, string Excerpt, ShowTellDirection Direction, string Suggestion);

// The manuscript's length against its genre norm. WordCount is the real count (computed deterministically);
// TargetLow/TargetHigh are the model's genre-norm range; Note is the plain-language framing (LengthGap).
public sealed record LengthAssessment(int WordCount, int TargetLow, int TargetHigh, string Note)
{
    public static LengthAssessment Empty { get; } = new(0, 0, 0, string.Empty);
}

// One chapter's dramatic tension, 0 (calm) to 1 (peak), for the pacing chart.
public sealed record ChapterTension(string Chapter, double Tension);

// The candid state-of-the-draft that closes the report: what is working best, what is most broken, and
// the single thing this draft most needs before the next revision.
public sealed record Verdict(
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Problems,
    string MostNeeds)
{
    public static Verdict Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>(), string.Empty);

    /// <summary>
    /// The clause the verdict page opens with — "The book is close." — and the paragraph under it.
    ///
    /// <para>They are what an author actually carries away. Everything else on the page is a list,
    /// and a list is read once; a sentence short enough to repeat to yourself is the one that
    /// survives the reading and decides whether the work gets done. The page was drawn around it
    /// from the start, and the renderer had been building the page without it.</para>
    ///
    /// <para>Init-only with an empty default, like every other field added after a report was
    /// stored: a verdict written before this existed opens with its strengths, which is the page
    /// that shipped rather than a hole in it.</para>
    /// </summary>
    public string Standing { get; init; } = string.Empty;

    public string Reading { get; init; } = string.Empty;
}

// The authorial-voice-consistency read: a short characterization of the book's dominant voice, and the
// passages whose prose drifts from it. This is about the manuscript's consistency WITH ITSELF — a chapter
// that slips into a different register, a tonal wobble — not a comparison to anything external.
public sealed record VoiceConsistency(string DominantVoice, IReadOnlyList<VoiceDivergence> Divergences)
{
    public static VoiceConsistency Empty { get; } = new(string.Empty, Array.Empty<VoiceDivergence>());
}

// One passage that reads as off from the book's established voice. Chapter locates it; Excerpt quotes the
// divergent prose verbatim so the author can find it; Divergence says how it departs from the dominant
// voice (register, diction, cadence, tone); Suggestion says how to bring it back into line.
public sealed record VoiceDivergence(string Chapter, string Excerpt, string Divergence, string Suggestion);

// A representative prose sample from one chapter, fed to the voice-consistency read so the model judges
// the actual writing rather than a summary of it.
public sealed record ChapterSample(string Chapter, string Excerpt);
