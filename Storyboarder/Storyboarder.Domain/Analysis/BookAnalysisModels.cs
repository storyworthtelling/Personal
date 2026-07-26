using System.Text.Json;
using System.Text.Json.Serialization;

namespace Storyboarder.Domain.Analysis;

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
[JsonConverter(typeof(FindingJsonConverter))]
public sealed record Finding(string Chapter, string Detail)
{
    public static Finding Of(string detail) => new(string.Empty, detail);
}

public sealed class FindingJsonConverter : JsonConverter<Finding>
{
    private const string ChapterProperty = "chapter";
    private const string DetailProperty = "detail";

    public override Finding Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return Finding.Of(reader.GetString() ?? string.Empty);

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException(
                $"A report finding must be a JSON string or object, but was {reader.TokenType}.");

        var chapter = string.Empty;
        var detail = string.Empty;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return new Finding(chapter, detail);
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
        }

        throw new JsonException("A report finding object was not terminated.");
    }

    public override void Write(Utf8JsonWriter writer, Finding value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(ChapterProperty, value.Chapter ?? string.Empty);
        writer.WriteString(DetailProperty, value.Detail ?? string.Empty);
        writer.WriteEndObject();
    }
}

// A character's place in the story: role, arc, and key relationships (rendered as the character map).
public sealed record CharacterProfile(
    string Name,
    string Role,
    string ArcSummary,
    IReadOnlyList<string> Relationships);

// One review from a specific, fair perspective (e.g. developmental editor, target-genre reader,
// literary critic, acquiring agent) with balanced strengths and weaknesses.
public sealed record Review(
    string Perspective,
    string Assessment,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Weaknesses,
    string Recommendation);

// One actionable revision suggestion. The report lists these highest-impact-first.
public sealed record RevisionItem(
    string Priority,
    string Chapter,
    string Issue,
    string SuggestedFix);

// One sentence (or short passage) to cut, tighten, or rewrite, quoted so the author can find it.
public sealed record LineEdit(
    string Chapter,
    string Excerpt,
    string Suggestion,
    string Reason);

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
    string Fix);

// One scene in the scene-by-scene map: what happens, what it accomplishes for the book, and a
// verdict — which is what surfaces the scenes doing no work.
public sealed record SceneBeat(
    string Chapter,
    string Scene,
    string Purpose,
    string Verdict);

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
    IReadOnlyList<Finding> PlotHoles,
    IReadOnlyList<Finding> Inconsistencies,
    IReadOnlyList<Finding> Inaccuracies,
    IReadOnlyList<Finding> PacingNotes,
    IReadOnlyList<Review> Reviews,
    IReadOnlyList<RevisionItem> RevisionPlan,
    IReadOnlyList<string> ComparableTitles,
    string MarketPositioning,
    IReadOnlyList<LineEdit> LineEdits,
    IReadOnlyList<ProseNote> ProseNotes,
    IReadOnlyList<DialogueNote> DialogueNotes,
    IReadOnlyList<SceneBeat> SceneBreakdown)
{
    // Verified factual claims. Arrives from its own call (the checks need web search, which the other
    // reduce calls must not have) and is folded in the same way the craft half is.
    public IReadOnlyList<FactCheck> FactChecks { get; init; } = Array.Empty<FactCheck>();

    // Where the author's own prose drifts from the book's dominant authorial voice. Arrives from its own
    // call (it needs actual prose samples per chapter, not the notes the other reduces work from) and is
    // folded in the same way. Defaults to Empty so a report stored before this existed re-renders fine.
    public VoiceConsistency VoiceConsistency { get; init; } = VoiceConsistency.Empty;

    public static BookAnalysisResult Empty { get; } = new(
        string.Empty, string.Empty, Array.Empty<CharacterProfile>(), Array.Empty<string>(),
        Array.Empty<Finding>(), Array.Empty<Finding>(), Array.Empty<Finding>(), Array.Empty<Finding>(),
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

    public BookAnalysisResult WithFactChecks(IReadOnlyList<FactCheck> checks) =>
        this with { FactChecks = checks };

    public BookAnalysisResult WithVoiceConsistency(VoiceConsistency voice) =>
        this with { VoiceConsistency = voice };
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

// One checkable real-world statement from the manuscript, put to a web search. Verdict is the model's
// call after searching; Note carries what it found. Chapter is copied from the note the claim came from
// so the author can find the sentence. A claim the search couldn't settle is reported as unverified
// rather than guessed at — a confidently wrong "incorrect" would send the author chasing a non-problem.
public sealed record FactCheck(string Claim, string Verdict, string Note, string Chapter);
