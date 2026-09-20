using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Folio.Domain.Analysis;
using Folio.Domain.Reading;
using Folio.Domain.External;
using Folio.Domain.Health;
using Folio.Domain.Model;
using Folio.Domain.Pipeline;
using Folio.Domain.Text;
using Folio.Infrastructure.Configuration;
using Storyworthtelling.Voice.Health;

namespace Folio.Infrastructure.External;

// Literary analysis via the Anthropic Messages API. Modeled on the repo's existing Claude client:
// x-api-key + anthropic-version headers, a prompt-cached system instruction, and descriptive
// failures. Provider throttling is surfaced as RateLimitException so the worker can reschedule.
public sealed class ClaudeAnalysisClient : IClaudeAnalysisClient
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private const string ExtractionInstruction =
        "You are a visual-development analyst preparing a graphic-novel design bible. Your list becomes the " +
        "illustrator's reference sheets, so any character or place you omit gets drawn from scratch and " +
        "breaks visual consistency. Be EXHAUSTIVE. List EVERY CHARACTER that appears or is described — " +
        "named or unnamed, major, minor, background, or a briefly-glimpsed walk-on (e.g. \"a scarred " +
        "innkeeper\", \"two guards at the gate\") — and EVERY SETTING/LOCATION/SET that appears or is " +
        "described, including interiors, exteriors, vehicles, and distinct rooms or areas within a larger " +
        "place. Do not skip an entity just because it seems unimportant or appears only once. Give each " +
        "concrete visual details useful for an illustrator (appearance, clothing, age, build for " +
        "characters; architecture, era, mood, palette, notable props for settings). For each entity also " +
        "give its approximate physical SCALE so the illustrations stay proportionate: for a character an " +
        "approximate height and build relative to an average adult; for a setting its approximate size " +
        "and any notable scale cues. Respond with ONLY a JSON object: " +
        "{\"characters\":[{\"name\":string,\"description\":string,\"scale\":string,\"aliases\":[string]}]," +
        "\"settings\":[{\"name\":string,\"description\":string,\"scale\":string,\"aliases\":[string]}]}. " +
        "Use [] when none are present, and \"\" for an unknown scale. No prose outside the JSON.";

    private const string ArtStyleInstruction =
        "You are an art director choosing the visual style for illustrated evidence images of a novel's " +
        "scenes. From the prose sample and the cast below, infer the book's genre, tone, period and " +
        "setting, then describe ONE cohesive illustration style for the WHOLE book — medium, rendering, " +
        "linework, colour palette, lighting and mood — in a single vivid sentence an illustrator could " +
        "follow consistently. Respond with ONLY a JSON object: {\"artStyle\":string}. No prose outside the JSON.";

    private const string SceneImageInstruction =
        "You are a visual-development artist illustrating ONE scene of a novel for a fidelity check: the " +
        "image will be placed beside the prose to test whether the scene's key beat lands visually AS " +
        "WRITTEN. From the scene text and the cast/settings below, choose the scene's EMOTIONAL PEAK — the " +
        "single most important dramatic moment — and describe one wordless illustration of it: who is " +
        "present, what they are doing, where, and the mood, grounded in the details the prose actually " +
        "gives. Depict a SPECIFIC moment of action or feeling, not a posed portrait. If a list of MOMENTS " +
        "TO AVOID is given, choose a DIFFERENT beat of the same scene. Put NO lettering, text, or speech " +
        "in the image. Respond with ONLY the visual description as plain text — no preamble, no quotes, no JSON.";

    private const string ChapterAnalysisInstruction =
        "You are a developmental editor analyzing ONE excerpt of a novel manuscript to help the author " +
        "revise toward publication. Use the KNOWN CAST names consistently when they appear. LOCATION IN " +
        "THE BOOK names the chapter(s) this excerpt covers — every observation you make is about that " +
        "part of the book, so keep each one specific enough to be found there again. STORY SO FAR is a " +
        "running summary of everything before this excerpt — use it to judge continuity, timeline, and " +
        "whether setups planted earlier pay off here. Read the excerpt and record concise, concrete, " +
        "accurate observations. Respond with ONLY a JSON object: " +
        "{\"charactersPresent\":[string],\"keyEvents\":[string],\"characterBeats\":[string]," +
        "\"setupsAndPayoffs\":[string],\"possibleInconsistencies\":[string],\"factualClaims\":[string]," +
        "\"notableStrengths\":[string],\"notableWeaknesses\":[string],\"lineEditCandidates\":[string]," +
        "\"proseObservations\":[string],\"dialogueObservations\":[string],\"sceneBeats\":[string]," +
        "\"summarizedMoments\":[string],\"thinSpots\":[string],\"offPageEvents\":[string]," +
        "\"runningState\":string}. runningState = the UPDATED story-so-far after this excerpt: extend STORY " +
        "SO FAR with this excerpt's plot developments, the current state of each character, open questions, " +
        "and planted-but-unpaid setups — a few tight paragraphs, cumulative, so the next excerpt inherits it. " +
        "characterBeats = decisions, " +
        "changes, or motivation shifts; setupsAndPayoffs = planted elements or promises (note if paid " +
        "off here); factualClaims = checkable real-world statements that may need fact-checking; " +
        "possibleInconsistencies = anything that may contradict earlier text (names, timeline, physical " +
        "details, world rules). " +
        // The craft half of the read. It has to happen here, while the actual sentences are in front of
        // the model: by consolidation time only these notes survive, so a line edit not quoted now can
        // never be quoted at all.
        "lineEditCandidates = specific sentences to cut, tighten, or rewrite — QUOTE the sentence " +
        "verbatim, then say what to do with it and why; proseObservations = prose-style and voice " +
        "patterns (repetition, filter words like \"she felt\"/\"he saw\", verbal tics, monotonous " +
        "sentence rhythm, overused constructions, slips out of the established POV) — quote a short " +
        "example for each; dialogueObservations = whether the speakers sound distinct from one another, " +
        "on-the-nose exchanges, and exposition dumped into speech — name the speakers; sceneBeats = one " +
        "line per SCENE in reading order, in the form \"<what happens> | <what it accomplishes for the " +
        "book> | <does it earn its place>\". " +
        // The raw material for the additive read (the author's drafts run short). Gathered here while the
        // prose is in front of the model; the expansion reduce turns it into scenes-to-add and show-vs-tell.
        "summarizedMoments = passages TOLD in summary that could become dramatized SCENES (the main lever " +
        "for adding words) — quote or name each; thinSpots = beats that run rushed or over-compressed and " +
        "could be expanded; offPageEvents = important turns that happen OFF the page (referred to but never " +
        "shown), which may deserve their own scene. " +
        "Use [] when none are present. Give AT MOST 12 items in each list, the most significant ones. " +
        "No prose outside the JSON.";

    private const string ConsolidationInstruction =
        "You are a panel of professional readers producing the STORY half of a REVISION REPORT for an " +
        "author preparing a novel for publication. You are given per-chunk notes gathered across the " +
        "whole manuscript in reading order, plus the cast. Each note carries the chapter it was taken " +
        "from. Synthesize the WHOLE book (do not merely concatenate the notes); " +
        "be accurate, fair, specific, and constructive. Judge the draft QUALITATIVELY — never assign a " +
        "numeric score or grade. Respond with ONLY a JSON object: " +
        "{\"logline\":string,\"synopsis\":string,\"genre\":string,\"wordCountRange\":string," +
        "\"characters\":[{\"name\":string,\"role\":string," +
        "\"arcSummary\":string,\"relationships\":[string]}],\"themes\":[string],\"plotHoles\":[FINDING]," +
        "\"inconsistencies\":[FINDING],\"inaccuracies\":[FINDING],\"pacingNotes\":[FINDING]," +
        "\"pacingCurve\":[{\"chapter\":string,\"tension\":number}],\"reviews\":" +
        "[{\"perspective\":string,\"assessment\":string,\"strengths\":[string],\"weaknesses\":[string]," +
        "\"recommendation\":string}],\"revisionPlan\":[{\"priority\":string,\"chapter\":string," +
        "\"issue\":string,\"suggestedFix\":string}]," +
        "\"verdict\":{\"standing\":string,\"reading\":string,\"strengths\":[string]," +
        "\"problems\":[string],\"mostNeeds\":string}}. " +
        // Every finding has to be findable: an observation the author can't locate in the manuscript
        // costs them a re-read to act on. The chapter is already on the notes, so it only has to be
        // carried through rather than worked out.
        "FINDING is an object {\"chapter\":string,\"detail\":string}: copy \"chapter\" from the note the " +
        "observation came from (\"Chapter 7\", \"Chapters 7–8\"), naming the EARLIEST chapter involved " +
        "when an observation spans several, and use \"\" only when the notes carry no chapter at all. " +
        "Set \"chapter\" on each revisionPlan item the same way. " +
        "genre: infer the book's genre. wordCountRange: the word-count range publishers expect for that " +
        "genre (e.g. \"90,000–120,000\"). " +
        "pacingCurve: one entry PER CHAPTER in reading order with tension from 0.0 (calm) to 1.0 (peak), so " +
        "a saggy stretch is visible; copy the chapter label from the notes. " +
        "Provide AT LEAST FOUR reviews from DISTINCT, fair perspectives — a DEVELOPMENTAL EDITOR (structure, " +
        "arcs, stakes), the TARGET-GENRE READER (where they're gripped, bored, confused, moved), the SKEPTIC " +
        "(the harshest fair read), and an AGENT & ACQUIRING EDITOR (is it sellable, is it ready, where it " +
        "sits in the market, and a readiness read: early draft / revision / near-submittable) — differing " +
        "but honest, each with real strengths and weaknesses. " +
        "Order revisionPlan HIGHEST-IMPACT FIRST and set priority to \"Must\", \"Should\", or \"Consider\". " +
        "verdict: the candid state of the draft — biggest strengths, biggest problems, and the single thing " +
        "this draft most needs before the next revision. Use [] when none. No prose outside the JSON.";

    private const string CraftConsolidationInstruction =
        "You are a line editor and craft critic producing the CRAFT half of a REVISION REPORT for an " +
        "author preparing a novel for publication. You are given per-chunk notes gathered across the " +
        "whole manuscript in reading order; each carries the chapter it was taken from, and " +
        "lineEditCandidates/proseObservations/dialogueObservations/sceneBeats are the raw material for " +
        "your four sections. Consolidate them into the patterns and edits that matter for the WHOLE " +
        "book — merge duplicates, drop the trivial, keep the author's own words when quoting. Respond " +
        "with ONLY a JSON object: " +
        "{\"lineEdits\":[{\"chapter\":string,\"excerpt\":string,\"suggestion\":string,\"reason\":string}]," +
        "\"proseNotes\":[{\"chapter\":string,\"pattern\":string,\"evidence\":string,\"fix\":string}]," +
        "\"dialogueNotes\":[{\"chapter\":string,\"speakers\":string,\"observation\":string,\"fix\":string}]," +
        "\"sceneBreakdown\":[{\"chapter\":string,\"scene\":string,\"purpose\":string,\"verdict\":string}]}. " +
        "lineEdits: specific sentences to cut, tighten, or rewrite — \"excerpt\" quoted verbatim from " +
        "the notes so the author can find it, \"suggestion\" the actual replacement or cut, \"reason\" " +
        "one short clause. AT MOST 40, chosen for impact. " +
        "proseNotes: recurring prose-style and voice patterns — repetition, filter words, verbal tics, " +
        "monotonous sentence rhythm, overused constructions, POV slips — with \"evidence\" quoting a " +
        "real example. AT MOST 20. " +
        "dialogueNotes: whether the characters sound distinct from one another, on-the-nose exchanges, " +
        "and exposition dumped into speech. AT MOST 20. " +
        "sceneBreakdown: the scene-by-scene map in reading order — \"scene\" what happens, \"purpose\" " +
        "what it accomplishes for the book (plot, character, theme, information), \"verdict\" whether it " +
        "earns its place and what is missing if not. Say plainly when a scene does no work. AT MOST 100. " +
        "Every \"chapter\" is copied from the note the item came from; \"\" only when the notes carry " +
        "none. Use [] when none. No prose outside the JSON.";

    private const string VoiceConsistencyInstruction =
        "You are a developmental editor assessing a manuscript's VOICE CONSISTENCY for an author preparing " +
        "it for publication. You are given a representative prose sample from each chapter, each tagged " +
        "with its chapter. First infer the book's DOMINANT authorial voice — its register, diction, " +
        "sentence cadence, tone, and narrative stance — from the body of samples. Then flag the passages " +
        "whose prose DRIFTS from that dominant voice: a chapter that slips into a different register, an " +
        "out-of-place tonal shift, diction or rhythm unlike the rest. Judge consistency of VOICE only — " +
        "not quality, plot, or subject matter. A deliberately distinct voice (a different POV character's " +
        "chapter, a letter or document embedded in the text) is NOT a defect, so flag only drift that " +
        "reads as unintentional. Respond with ONLY a JSON object: " +
        "{\"dominantVoice\":string,\"divergences\":[{\"chapter\":string,\"excerpt\":string," +
        "\"divergence\":string,\"suggestion\":string}]}. dominantVoice: two or three sentences " +
        "characterizing the book's established voice. excerpt: quote the divergent prose VERBATIM from the " +
        "sample so the author can find it. divergence: how it departs from the dominant voice. suggestion: " +
        "how to bring it into line. Report AT MOST 20 divergences, the clearest ones, and use [] when the " +
        "voice is consistent throughout. No prose outside the JSON.";

    private const string ReconciliationInstruction =
        "You are reconciling a manuscript's revision findings ACROSS DRAFTS. You are given the PRIOR draft's " +
        "findings (each with a stable id, category, chapter, and text) and the CURRENT draft's findings (each " +
        "with a numeric index). For each CURRENT finding, decide whether it is the SAME underlying issue as one " +
        "of the prior findings — the same plot hole, inconsistency, inaccuracy, or pacing problem — even if the " +
        "wording, phrasing, or chapter citation has moved between drafts, or whether it is a genuinely NEW " +
        "issue. Match CONSERVATIVELY: only when it is really the same issue, not merely a similar topic or the " +
        "same chapter. Respond with ONLY a JSON object: {\"matches\":[{\"index\":int,\"threadId\":string|null}]}. " +
        "index: the current finding's index. threadId: the id of the prior finding it is the same issue as, or " +
        "null when it is new. Include exactly one entry per current finding, and never map two current findings " +
        "to the same prior id. No prose outside the JSON.";

    private const string ExpansionInstruction =
        "You are a developmental editor helping an author whose early drafts run SHORT. Your job here is " +
        "ADDITIVE: find what the book is missing and where it could grow. You are given the per-chunk notes " +
        "(each chunk's summarized moments, off-page events, thin spots, character beats, and setups), a " +
        "running story-state, the manuscript's actual WORD COUNT, and its GENRE. You NUDGE, you never write: " +
        "every suggestion is a POINTER the author acts on themselves — never drafted prose, never an example " +
        "scene, never sample lines. Respond with ONLY a JSON object: " +
        "{\"scenesToAdd\":[{\"kind\":string,\"location\":string,\"missing\":string,\"why\":string," +
        "\"priority\":string}],\"showVsTell\":[{\"chapter\":string,\"excerpt\":string,\"direction\":string," +
        "\"suggestion\":string}],\"targetLow\":number,\"targetHigh\":number}. " +
        "scenesToAdd = the scenes/beats to ADD, ranked most-impactful first. kind is one of " +
        "\"MissingBeat\" (a turn that happens off-page), \"CharacterDeepening\", \"TensionStakes\", or " +
        "\"ThemeDeepening\". location says where it belongs (\"Chapter 4\", \"after Chapter 7\"). missing = " +
        "what the reader never gets to see. why = the impact of adding it. priority is one of \"Must\", " +
        "\"Should\", \"Consider\". showVsTell = show-vs-tell notes on BOTH ends: direction \"Dramatize\" for a " +
        "SUMMARIZED passage that could become a shown scene (the main lever for adding words) or \"Tighten\" " +
        "for an OVER-shown passage that runs long; quote the excerpt VERBATIM and give a pointer, not a " +
        "rewrite. targetLow/targetHigh = the typical published word-count range for this genre (integers; use " +
        "0/0 if you can't judge it). Give AT MOST 15 scenesToAdd and 15 showVsTell, the most significant, and " +
        "use [] when there are none. No prose outside the JSON.";

    private const string SeriesContinuityInstruction =
        "You are a continuity editor checking a novel that is a LATER BOOK in a series against the canon its " +
        "PRIOR book established. You are given the prior book's canon — its cast and settings with the " +
        "descriptions the prior book established, where its story stood at the end, and the unresolved problems " +
        "it left open — and this book's per-chunk notes and running story state. Do TWO things. (1) Find " +
        "CONTRADICTIONS: places where this book contradicts what the prior book established — a character " +
        "described or behaving inconsistently with their canon, a place / world rule / timeline that clashes, a " +
        "fact that cannot be true if the prior book's was. Be specific and CONSERVATIVE: a deliberate " +
        "development, reveal, or change over time is NOT a contradiction — flag only genuine clashes. (2) For " +
        "each of the prior book's OPEN THREADS, judge whether this book ADDRESSES it (resolves or meaningfully " +
        "advances it), IGNORES it (never touches it), or CONTRADICTS it. You NUDGE, you never rewrite: point at " +
        "the problem, never draft the fix. Respond with ONLY a JSON object: " +
        "{\"breaks\":[{\"chapter\":string,\"established\":string,\"contradiction\":string}]," +
        "\"carriedThreads\":[{\"established\":string,\"status\":string}]}. breaks: chapter = where in THIS book " +
        "(\"Chapter 4\", \"\" if unknown); established = what the prior book set; contradiction = what this book " +
        "says instead. carriedThreads: established = the prior open thread verbatim; status = one of " +
        "\"Addressed\", \"Ignored\", \"Contradicted\". Give one carriedThreads entry per open thread provided. " +
        "Use [] when there are no breaks. No prose outside the JSON.";

    private readonly HttpClient httpClient;
    private readonly ClaudeOptions options;
    private readonly ICredentialHealthSink? healthSink;

    public ClaudeAnalysisClient(
        HttpClient httpClient, IOptions<ClaudeOptions> options, ICredentialHealthSink? healthSink = null)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.healthSink = healthSink;
    }

    public async Task<DesignBibleFragment> ExtractEntities(string chunkText, CancellationToken cancellationToken)
    {
        // Strong tier: this call decides how many reference sheets get drawn, and the cheap model
        // under-extracts — it drops minor characters and described sets, leaving those panels with no
        // reference so the illustrator invents faces and relocates scenes. Coverage is worth the strong
        // model here even though it runs once per chunk.
        var reply = await SendMessage(
            ExtractionInstruction, chunkText, cancellationToken, options.ExtractionMaxTokens, model: options.Model);
        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
            return DesignBibleFragment.Empty;

        var dto = JsonResponse.Deserialize<FragmentDto>(json);
        if (dto is null)
            return DesignBibleFragment.Empty;

        return new DesignBibleFragment(MapMentions(dto.Characters), MapMentions(dto.Settings));
    }

    public async Task<string> DeriveArtStyle(string sampleText, DesignBible bible, CancellationToken cancellationToken)
    {
        var bibleJson = JsonSerializer.Serialize(bible, JsonResponse.CaseInsensitive);
        var userContent = $"CAST & SETTINGS (JSON):\n{bibleJson}\n\nPROSE SAMPLE:\n{sampleText}";

        // Best-effort: a malformed or empty reply yields an empty style and the caller keeps the default,
        // so a bad derivation never blocks the book.
        var reply = await SendMessage(ArtStyleInstruction, userContent, cancellationToken);
        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
            return string.Empty;
        var dto = JsonResponse.Deserialize<ArtStyleDto>(json);
        return dto?.ArtStyle?.Trim() ?? string.Empty;
    }

    public async Task<string> DescribeSceneImage(
        string sceneText, DesignBible bible, string artStyle, IReadOnlyList<string> avoidMoments,
        CancellationToken cancellationToken)
    {
        var bibleJson = JsonSerializer.Serialize(bible, JsonResponse.CaseInsensitive);
        var avoid = avoidMoments.Count == 0
            ? "(none)"
            : string.Join("\n", avoidMoments.Select(m => $"- {m}"));
        var userContent =
            $"ART STYLE: {artStyle}\n\n" +
            $"CAST & SETTINGS (JSON):\n{bibleJson}\n\n" +
            $"MOMENTS TO AVOID:\n{avoid}\n\n" +
            $"SCENE:\n{sceneText}";

        // Best-effort: a wordless prompt is nice to have, so any empty/failed reply yields an empty prompt
        // and the caller leaves the scene without an image rather than blocking the report.
        var reply = await SendMessage(SceneImageInstruction, userContent, cancellationToken);
        return reply.Trim();
    }

    public async Task<ChapterNotes> AnalyzeChapter(
        string chapterText, string chapterLabel, IReadOnlyList<string> characterNames,
        string priorRunningState, CancellationToken cancellationToken)
    {
        var cast = characterNames.Count == 0 ? "(none identified yet)" : string.Join(", ", characterNames);
        var location = string.IsNullOrWhiteSpace(chapterLabel) ? "(unknown)" : chapterLabel;
        var storySoFar = string.IsNullOrWhiteSpace(priorRunningState) ? "(this is the beginning)" : priorRunningState;
        var userContent =
            $"LOCATION IN THE BOOK: {location}\n\nKNOWN CAST: {cast}\n\n" +
            $"STORY SO FAR (running state up to this excerpt):\n{storySoFar}\n\nEXCERPT:\n{chapterText}";

        // Fast tier: one call per chunk, recording observations against a fixed JSON contract. The
        // judgement calls — synthesising these notes into a report — stay on the strong model.
        var reply = await SendMessage(
            ChapterAnalysisInstruction, userContent, cancellationToken, options.ChapterNotesMaxTokens,
            model: options.FastModel);
        var json = JsonResponse.ExtractObject(reply);
        // The chapter is known exactly from the chunk's offsets, so it is stamped on here rather than
        // trusted to the model — even a reply that failed to parse still carries where it came from, and
        // keeps the prior running state so a parse failure doesn't wipe the story-so-far.
        var located = ChapterNotes.Empty with { Chapter = chapterLabel, RunningState = priorRunningState };
        if (json is null)
            return located;

        var dto = JsonResponse.Deserialize<ChapterNotesDto>(json);
        if (dto is null)
            return located;

        return new ChapterNotes(
            chapterLabel,
            dto.CharactersPresent ?? [], dto.KeyEvents ?? [], dto.CharacterBeats ?? [],
            dto.SetupsAndPayoffs ?? [], dto.PossibleInconsistencies ?? [], dto.FactualClaims ?? [],
            dto.NotableStrengths ?? [], dto.NotableWeaknesses ?? [],
            dto.LineEditCandidates ?? [], dto.ProseObservations ?? [],
            dto.DialogueObservations ?? [], dto.SceneBeats ?? [])
        {
            // The updated story-so-far the model returns, threaded to the next chunk. Falls back to the prior
            // state if the model omitted it, so the running summary never regresses to empty mid-book.
            RunningState = string.IsNullOrWhiteSpace(dto.RunningState) ? priorRunningState : dto.RunningState!,
            // The raw material for the expansion engine, carried through to the additive reduce.
            SummarizedMoments = dto.SummarizedMoments ?? [],
            ThinSpots = dto.ThinSpots ?? [],
            OffPageEvents = dto.OffPageEvents ?? []
        };
    }

    public async Task<BookAnalysisResult> ConsolidateAnalysis(
        string title, IReadOnlyList<ChapterNotes> notes, DesignBible bible, string continuitySummary,
        CancellationToken cancellationToken)
    {
        var notesJson = JsonSerializer.Serialize(notes, JsonResponse.CaseInsensitive);
        var bibleJson = JsonSerializer.Serialize(bible, JsonResponse.CaseInsensitive);
        var userContent =
            $"TITLE: {title}\n\n" +
            $"CAST (design bible JSON):\n{bibleJson}\n\n" +
            $"CONTINUITY SUMMARY:\n{(string.IsNullOrWhiteSpace(continuitySummary) ? "(none)" : continuitySummary)}\n\n" +
            $"PER-CHUNK NOTES (JSON array, in reading order):\n{notesJson}";

        var reply = await SendMessage(ConsolidationInstruction, userContent, cancellationToken, options.AnalysisMaxTokens);
        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
            return BookAnalysisResult.Empty;

        var dto = JsonResponse.Deserialize<BookAnalysisDto>(json);
        if (dto is null)
            return BookAnalysisResult.Empty;

        return new BookAnalysisResult(
            dto.Logline ?? string.Empty,
            dto.Synopsis ?? string.Empty,
            (dto.Characters ?? []).Select(c =>
                new CharacterProfile(c.Name ?? string.Empty, c.Role ?? string.Empty, c.ArcSummary ?? string.Empty, c.Relationships ?? [])).ToList(),
            dto.Themes ?? [],
            dto.PlotHoles ?? [],
            dto.Inconsistencies ?? [],
            dto.Inaccuracies ?? [],
            dto.PacingNotes ?? [],
            // A read that arrives short is dropped rather than coalesced into a thinner one: the
            // ?? string.Empty this replaces is precisely how a persona that answered four of six
            // questions produced a page that rendered perfectly and said less (F44, F46). What the
            // pipeline does about a dropped read is the pipeline's decision, not the parser's.
            (dto.Reviews ?? []).Select(r => new Review(
                    r.PersonaId ?? r.Perspective ?? string.Empty,
                    r.Verdict ?? string.Empty,
                    r.Assessment ?? string.Empty,
                    r.Strengths ?? [],
                    r.Weaknesses ?? [],
                    r.Condition ?? r.Recommendation ?? string.Empty))
                .Where(ReaderRead.IsWhole).ToList(),
            (dto.RevisionPlan ?? []).Select(r =>
                new RevisionItem(r.Priority ?? string.Empty, r.Chapter ?? string.Empty,
                    r.Issue ?? string.Empty, r.SuggestedFix ?? string.Empty)).ToList(),
            dto.ComparableTitles ?? [],
            dto.MarketPositioning ?? string.Empty,
            // The craft sections come from their own call (ConsolidateCraft) and are folded in by the
            // caller; this half of the report leaves them empty rather than half-answering them.
            CraftReport.Empty.LineEdits, CraftReport.Empty.ProseNotes,
            CraftReport.Empty.DialogueNotes, CraftReport.Empty.SceneBreakdown)
        {
            Genre = dto.Genre ?? string.Empty,
            WordCountRange = dto.WordCountRange ?? string.Empty,
            PacingCurve = (dto.PacingCurve ?? [])
                .Where(t => !string.IsNullOrWhiteSpace(t.Chapter))
                .Select(t => new ChapterTension(t.Chapter!, Math.Clamp(t.Tension, 0, 1)))
                .ToList(),
            Verdict = dto.Verdict is null
                ? Verdict.Empty
                : new Verdict(dto.Verdict.Strengths ?? [], dto.Verdict.Problems ?? [], dto.Verdict.MostNeeds ?? string.Empty)
                {
                    Standing = dto.Verdict.Standing ?? string.Empty,
                    Reading = dto.Verdict.Reading ?? string.Empty,
                }
        };
    }

    public async Task<CraftReport> ConsolidateCraft(
        string title, IReadOnlyList<ChapterNotes> notes, CancellationToken cancellationToken)
    {
        var notesJson = JsonSerializer.Serialize(notes, JsonResponse.CaseInsensitive);
        var userContent =
            $"TITLE: {title}\n\n" +
            $"PER-CHUNK NOTES (JSON array, in reading order):\n{notesJson}";

        var reply = await SendMessage(
            CraftConsolidationInstruction, userContent, cancellationToken, options.CraftMaxTokens);
        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
            return CraftReport.Empty;

        var dto = JsonResponse.Deserialize<CraftReportDto>(json);
        if (dto is null)
            return CraftReport.Empty;

        return new CraftReport(
            (dto.LineEdits ?? []).Select(e => new LineEdit(
                e.Chapter ?? string.Empty, e.Excerpt ?? string.Empty,
                e.Suggestion ?? string.Empty, e.Reason ?? string.Empty)).ToList(),
            (dto.ProseNotes ?? []).Select(n => new ProseNote(
                n.Chapter ?? string.Empty, n.Pattern ?? string.Empty,
                n.Evidence ?? string.Empty, n.Fix ?? string.Empty)).ToList(),
            (dto.DialogueNotes ?? []).Select(n => new DialogueNote(
                n.Chapter ?? string.Empty, n.Speakers ?? string.Empty,
                n.Observation ?? string.Empty, n.Fix ?? string.Empty)).ToList(),
            (dto.SceneBreakdown ?? []).Select(s => new SceneBeat(
                s.Chapter ?? string.Empty, s.Scene ?? string.Empty,
                s.Purpose ?? string.Empty, s.Verdict ?? string.Empty)).ToList());
    }

    public async Task<VoiceConsistency> AssessVoiceConsistency(
        IReadOnlyList<ChapterSample> samples, CancellationToken cancellationToken)
    {
        var meaningful = samples.Where(s => !string.IsNullOrWhiteSpace(s.Excerpt)).ToList();
        // A single chapter (or none) gives nothing to judge consistency against — don't spend a call.
        if (meaningful.Count < 2)
            return VoiceConsistency.Empty;

        var userContent = "CHAPTER SAMPLES (JSON):\n" + JsonSerializer.Serialize(meaningful, JsonResponse.CaseInsensitive);
        var reply = await SendMessage(VoiceConsistencyInstruction, userContent, cancellationToken, options.AnalysisMaxTokens);
        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
            return VoiceConsistency.Empty;

        var dto = JsonResponse.Deserialize<VoiceConsistencyDto>(json);
        if (dto is null)
            return VoiceConsistency.Empty;

        return new VoiceConsistency(
            dto.DominantVoice ?? string.Empty,
            (dto.Divergences ?? [])
                .Where(d => !string.IsNullOrWhiteSpace(d.Excerpt))
                .Select(d => new VoiceDivergence(
                    d.Chapter ?? string.Empty, d.Excerpt!, d.Divergence ?? string.Empty, d.Suggestion ?? string.Empty))
                .ToList());
    }

    public async Task<FindingReconciliation> ReconcileFindings(
        IReadOnlyList<PriorFinding> priorThreads, IReadOnlyList<CandidateFinding> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
            return FindingReconciliation.Empty;

        // With nothing to match against, every finding is new — no call to spend.
        if (priorThreads.Count == 0)
            return new FindingReconciliation(candidates.Select(c => new FindingMatch(c.Index, null)).ToList());

        var priorJson = JsonSerializer.Serialize(
            priorThreads.Select(p => new { id = p.ThreadId, category = p.Category.ToString(), chapter = p.Chapter, detail = p.Detail }),
            JsonResponse.CaseInsensitive);
        var currentJson = JsonSerializer.Serialize(
            candidates.Select(c => new { index = c.Index, category = c.Category.ToString(), chapter = c.Chapter, detail = c.Detail }),
            JsonResponse.CaseInsensitive);
        var userContent = $"PRIOR FINDINGS (JSON):\n{priorJson}\n\nCURRENT FINDINGS (JSON):\n{currentJson}";

        var reply = await SendMessage(ReconciliationInstruction, userContent, cancellationToken, options.AnalysisMaxTokens);
        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
            return FindingReconciliation.Empty;

        var dto = JsonResponse.Deserialize<ReconciliationDto>(json);
        if (dto?.Matches is null)
            return FindingReconciliation.Empty;

        return new FindingReconciliation(dto.Matches
            .Select(m => new FindingMatch(m.Index, Guid.TryParse(m.ThreadId, out var threadId) ? threadId : null))
            .ToList());
    }

    public async Task<ExpansionReport> ProposeExpansion(
        string title, IReadOnlyList<ChapterNotes> notes, string continuitySummary, int wordCount, string genre,
        CancellationToken cancellationToken)
    {
        var notesJson = JsonSerializer.Serialize(notes, JsonResponse.CaseInsensitive);
        var userContent =
            $"TITLE: {title}\n\n" +
            $"GENRE: {(string.IsNullOrWhiteSpace(genre) ? "(not inferred)" : genre)}\n\n" +
            $"WORD COUNT: {wordCount}\n\n" +
            $"STORY STATE:\n{(string.IsNullOrWhiteSpace(continuitySummary) ? "(none)" : continuitySummary)}\n\n" +
            $"PER-CHUNK NOTES (JSON array, in reading order):\n{notesJson}";

        // The length line is measured, not model-dependent, so it is produced even when the reply is
        // unusable — only the additions and the genre range degrade away.
        var noAdditions = new ExpansionReport(
            Array.Empty<SceneAddition>(), Array.Empty<ShowTellNote>(),
            new LengthAssessment(wordCount, 0, 0, LengthGap.Describe(wordCount, 0, 0)));

        var reply = await SendMessage(ExpansionInstruction, userContent, cancellationToken, options.AnalysisMaxTokens);
        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
            return noAdditions;

        var dto = JsonResponse.Deserialize<ExpansionDto>(json);
        if (dto is null)
            return noAdditions;

        var scenes = (dto.ScenesToAdd ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Missing))
            .Select(s => new SceneAddition(
                ParseAdditionKind(s.Kind), s.Location ?? string.Empty, s.Missing!, s.Why ?? string.Empty,
                NormalizePriority(s.Priority)))
            .ToList();
        var showTell = (dto.ShowVsTell ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n.Excerpt))
            .Select(n => new ShowTellNote(
                n.Chapter ?? string.Empty, n.Excerpt!, ParseShowTellDirection(n.Direction), n.Suggestion ?? string.Empty))
            .ToList();

        // The count is measured, not trusted to the model; the model supplies the genre range and the pure
        // LengthGap turns it into the framing line.
        var low = Math.Max(0, dto.TargetLow);
        var high = Math.Max(0, dto.TargetHigh);
        var length = new LengthAssessment(wordCount, low, high, LengthGap.Describe(wordCount, low, high));

        return new ExpansionReport(scenes, showTell, length);
    }

    public async Task<SeriesContinuity> AssessSeriesContinuity(
        string title, SeriesCanon priorCanon, IReadOnlyList<ChapterNotes> notes, string continuitySummary,
        CancellationToken cancellationToken)
    {
        var canonJson = JsonSerializer.Serialize(priorCanon, JsonResponse.CaseInsensitive);
        var notesJson = JsonSerializer.Serialize(notes, JsonResponse.CaseInsensitive);
        var userContent =
            $"THIS BOOK'S TITLE: {title}\n\n" +
            $"PRIOR BOOK'S CANON (JSON):\n{canonJson}\n\n" +
            $"THIS BOOK'S STORY STATE:\n{(string.IsNullOrWhiteSpace(continuitySummary) ? "(none)" : continuitySummary)}\n\n" +
            $"THIS BOOK'S PER-CHUNK NOTES (JSON array, in reading order):\n{notesJson}";

        // Best-effort: a malformed or empty reply yields no breaks/threads (keeping the prior book's title), so
        // the continuity read never blocks the report.
        var empty = SeriesContinuity.Empty with { PriorBookTitle = priorCanon.PriorBookTitle };

        var reply = await SendMessage(SeriesContinuityInstruction, userContent, cancellationToken, options.AnalysisMaxTokens);
        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
            return empty;

        var dto = JsonResponse.Deserialize<SeriesContinuityDto>(json);
        if (dto is null)
            return empty;

        var breaks = (dto.Breaks ?? [])
            .Where(b => !string.IsNullOrWhiteSpace(b.Contradiction))
            .Select(b => new SeriesBreak(b.Chapter ?? string.Empty, b.Established ?? string.Empty, b.Contradiction!))
            .ToList();
        var carried = (dto.CarriedThreads ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t.Established))
            .Select(t => new CarriedThread(t.Established!, NormalizeThreadStatus(t.Status)))
            .ToList();

        return new SeriesContinuity(priorCanon.PriorBookTitle, breaks, carried);
    }

    // Snap a carried-thread status to the three the report speaks; an unrecognized value reads as "Ignored"
    // (the conservative "still an open problem" verdict), so a stray value can't quietly report it resolved.
    private static string NormalizeThreadStatus(string? status)
    {
        var value = status?.Trim() ?? string.Empty;
        foreach (var known in new[] { "Addressed", "Ignored", "Contradicted" })
            if (string.Equals(value, known, StringComparison.OrdinalIgnoreCase))
                return known;
        return "Ignored";
    }

    private static AdditionKind ParseAdditionKind(string? kind) =>
        Enum.TryParse<AdditionKind>(kind, ignoreCase: true, out var parsed) ? parsed : AdditionKind.MissingBeat;

    private static ShowTellDirection ParseShowTellDirection(string? direction) =>
        Enum.TryParse<ShowTellDirection>(direction, ignoreCase: true, out var parsed) ? parsed : ShowTellDirection.Dramatize;

    // Snap a free-text priority to the three impact tiers the report groups by; anything unrecognized is the
    // lowest tier so a stray value can't masquerade as urgent.
    private static string NormalizePriority(string? priority)
    {
        var value = priority?.Trim() ?? string.Empty;
        foreach (var tier in new[] { "Must", "Should", "Consider" })
            if (string.Equals(value, tier, StringComparison.OrdinalIgnoreCase))
                return tier;
        return "Consider";
    }

    // model selects the tier: null means the strong default. Only the mechanical per-chunk calls pass the
    // fast one — see ClaudeOptions.FastModel for why those two and not the rest.
    // What each pass is asked to hand back. Kept beside the calls that use them so the shape the
    // parser expects and the shape the prompt asks for cannot drift apart.
    private const string SectionFormat =
        "Reply with JSON only: {\"summary\": \"one or two sentences on what you found overall, or an "
        + "empty string if this book gives you nothing to say\", \"findings\": [{\"chapter\": \"Ch. 12-15\", "
        + "\"detail\": \"what is wrong and why it matters\"}]}. Finding nothing is a legitimate answer: "
        + "return an empty findings list rather than inventing one.";

    private const string ReaderFormat =
        "Reply with JSON only: {\"verdict\": \"your answer in one clause\", \"assessment\": \"your full "
        + "review in character, several hundred words\", \"strengths\": [\"what works, in your terms\"], "
        + "\"weaknesses\": [\"what does not, in your terms\"], \"condition\": \"what would have to change "
        + "before your verdict changed\"}. Every field is required. If you cannot fill one of them you "
        + "have not finished reading: a short answer is worse than none, because the page prints it.";

    // Built rather than a const: the noun has to name what this manuscript actually is (a 1,200-word
    // short story is not "one novel"), and that depends on the measured word count these passes never
    // see the manuscript to derive for themselves.
    private static string RevisionPlanInstruction(StoryLength length) =>
        $"You are given fourteen independent reads of one {ManuscriptLength.Noun(length)}: eight structural "
        + "sections and six readers, none of whom saw the others' notes. Produce the author's revision plan. "
        + "Where several reads name the same thing, that is one item and it ranks higher for it. Reply with "
        + "JSON only: {\"revisionPlan\": [{\"priority\": \"Must|Should|Consider\", \"chapter\": \"Ch. 12-15\", "
        + "\"issue\": \"what is wrong\", \"suggestedFix\": \"what to do about it — a direction, never "
        + "replacement prose\"}]}. You diagnose and point; you never write the author's sentences for them.";

    private static string VerdictInstruction(StoryLength length) =>
        $"You are given fourteen independent reads of one {ManuscriptLength.Noun(length)}. Write the candid "
        + "state of this draft. "
        // "standing" is the line the author repeats to themselves afterwards, so it is asked for as a
        // sentence rather than as a rating: a page that opens on a number is read as a mark.
        + "Open with \"standing\": ONE short sentence naming where the draft stands (\"The book is "
        + "close.\"), and \"reading\": a paragraph that argues it. Then its biggest strengths, its "
        + "biggest problems, and the one thing it most needs. Reply with JSON only: {\"verdict\": "
        + "{\"standing\": \"\", \"reading\": \"\", \"strengths\": [], \"problems\": [], \"mostNeeds\": \"\"}}.";

    public async Task<SectionRead> ReadSection(
        Persona persona, string manuscript, CancellationToken cancellationToken)
    {
        var reply = await SendPass(persona, manuscript, SectionFormat, cancellationToken);
        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
        {
            // A section is allowed to find nothing, so an unparseable reply degrades to silence
            // rather than failing the read. The pass is still recorded as done: it ran, and a section
            // with nothing to say prints nothing.
            return SectionRead.Silent(persona.Id);
        }

        var dto = JsonResponse.Deserialize<SectionDto>(json);
        return new SectionRead(
            persona.Id,
            dto?.Summary ?? string.Empty,
            (dto?.Findings ?? []).Where(finding => !string.IsNullOrWhiteSpace(finding.Detail))
                .Select(finding => new ReportFinding(finding.Chapter ?? string.Empty, finding.Detail!))
                .ToList());
    }

    public async Task<Review> ReadAsReader(
        Persona persona, string manuscript, CancellationToken cancellationToken)
    {
        var reply = await SendPass(persona, manuscript, ReaderFormat, cancellationToken);
        var json = JsonResponse.ExtractObject(reply)
            ?? throw new InvalidOperationException(
                $"{persona.Name} replied with nothing this could read as a review. A reader's pass is "
                + "all-or-nothing: a page that prints half a read is worse than one that admits the "
                + "read failed.");

        var dto = JsonResponse.Deserialize<ReviewDto>(json);
        var review = new Review(
            persona.Id,
            dto?.Verdict ?? string.Empty,
            dto?.Assessment ?? string.Empty,
            dto?.Strengths ?? [],
            dto?.Weaknesses ?? [],
            dto?.Condition ?? dto?.Recommendation ?? string.Empty);

        // A short read is a failed pass, not a shorter one (F44, F46). Throwing here is what makes
        // that true: the pipeline retries the pass, where swallowing it would print the gap.
        if (ReaderRead.Incomplete(review) is { } reason)
        {
            throw new InvalidOperationException($"The read came back short — {reason}.");
        }
        return review;
    }

    public async Task<SynthesisRead> Synthesise(
        string passId,
        IReadOnlyList<SectionRead> sections,
        IReadOnlyList<Review> readers,
        int wordCount,
        CancellationToken cancellationToken)
    {
        // The synthesis passes read the other fourteen rather than the manuscript, so they carry no
        // cached prefix and cost nothing to run against either model.
        var evidence = JsonSerializer.Serialize(new { sections, readers }, JsonResponse.CaseInsensitive);
        var length = ManuscriptLength.Classify(wordCount);
        var instruction = passId == "verdict" ? VerdictInstruction(length) : RevisionPlanInstruction(length);
        var reply = await SendMessage(
            instruction, evidence, cancellationToken, options.PassMaxTokens, model: options.StrongModel);

        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
        {
            return SynthesisRead.Empty;
        }

        var dto = JsonResponse.Deserialize<SynthesisDto>(json);
        return new SynthesisRead(
            (dto?.RevisionPlan ?? []).Select(item => new RevisionItem(
                item.Priority ?? string.Empty, item.Chapter ?? string.Empty,
                item.Issue ?? string.Empty, item.SuggestedFix ?? string.Empty)).ToList(),
            dto?.Verdict is { } verdict
                ? new Verdict(verdict.Strengths ?? [], verdict.Problems ?? [], verdict.MostNeeds ?? string.Empty)
                {
                    Standing = verdict.Standing ?? string.Empty,
                    Reading = verdict.Reading ?? string.Empty,
                }
                : Verdict.Empty);
    }

    /// <summary>
    /// Sends one persona's pass, with the MANUSCRIPT FIRST and everything persona-specific after it.
    ///
    /// <para>It does not go through <see cref="SendMessage"/>, and that is the point. SendMessage puts
    /// its instruction in the first cached block, which is right when the instruction is constant for
    /// a call type and catastrophic here: the instruction names the persona, so seventeen passes would
    /// be seventeen different cache prefixes and the manuscript would be written seventeen times
    /// instead of read fifteen — measured at roughly $17 a draft against $2 (F50), with nothing
    /// visible wrong. <c>ReadingPrompt</c> decides the order and a guard holds it.</para>
    /// </summary>
    private Task<string> SendPass(
        Persona persona, string manuscript, string format, CancellationToken cancellationToken)
    {
        var system = ReadingPrompt.System(manuscript, persona)
            .Select(block => block.Cached
                ? (object)new { type = "text", text = block.Text, cache_control = new { type = "ephemeral" } }
                : new { type = "text", text = block.Text })
            .ToArray();

        var model = persona.Tier == ModelTier.Strong ? options.StrongModel : options.StandardModel;
        return SendRaw(system, ReadingPrompt.Question(persona) + "\n\n" + format,
            options.PassMaxTokens, model, cancellationToken);
    }

    private Task<string> SendMessage(
        string instruction, string userText, CancellationToken cancellationToken,
        int? maxTokens = null, string? cachedContext = null, string? model = null)
    {
        // Invariant, reused context (the design bible) is cached as its own block so repeated calls
        // within the cache window bill it as a cache read instead of full input. Both blocks share one
        // anonymous shape so the array stays strongly typed (no object-typed JSON serialization).
        //
        // The INSTRUCTION comes first here, which is safe only because it is constant for a given call
        // type — every ConsolidateAnalysis shares one instruction, so the prefix is shared. It is not
        // safe for the persona passes, whose instruction names the reader: see SendPass and F50.
        object[] system = string.IsNullOrEmpty(cachedContext)
            ? [new { type = "text", text = instruction, cache_control = new { type = "ephemeral" } }]
            :
            [
                new { type = "text", text = instruction, cache_control = new { type = "ephemeral" } },
                new { type = "text", text = cachedContext, cache_control = new { type = "ephemeral" } },
            ];

        return SendRaw(system, userText, maxTokens ?? options.MaxTokens,
            string.IsNullOrWhiteSpace(model) ? options.Model : model, cancellationToken);
    }

    // The transport both paths share: everything below this line is the same whatever the system
    // blocks say, so the ordering decision lives entirely in the two callers above.
    private async Task<string> SendRaw(
        object[] system, string userText, int effectiveMaxTokens, string model,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model,
            max_tokens = effectiveMaxTokens,
            stream = true,
            system,
            messages = new[]
            {
                new { role = "user", content = userText }
            }
        };

        var requestBody = JsonSerializer.SerializeToNode(payload)!.AsObject();

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Add("x-api-key", options.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        // Stream the response as server-sent events. A large storyboard generation runs well past the
        // default 100s HttpClient timeout, and a non-streaming request leaves the socket idle for those
        // minutes — mobile carriers/NATs then abort it, surfacing as "Socket closed" / "Software caused
        // connection abort". Streaming keeps bytes flowing so the connection stays alive, and reading
        // headers first means the long generation isn't bounded by the send timeout.
        var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            // Only a credential rejection flags the key; throttling/5xx/network are transient and left alone.
            if (AuthFailure.IsHttpCredentialRejection((int)response.StatusCode, body))
                healthSink?.MarkFailing(CredentialIds.Claude, $"{(int)response.StatusCode} {response.StatusCode}");
            throw ToException(response, body);
        }

        var text = await ReadStreamedText(response, effectiveMaxTokens, cancellationToken);
        // A successful authenticated call clears any prior failure on this key.
        healthSink?.MarkOk(CredentialIds.Claude);
        return text;
    }

    // Accumulates the assistant's text from an Anthropic Messages streaming response. Only text_delta
    // events contribute; a mid-stream error event is surfaced with the same classification as an HTTP
    // error so the worker reschedules throttling rather than crashing the cycle. The message_delta
    // event carries the final stop_reason: if the model ran out of output budget ("max_tokens") the
    // accumulated text is truncated, so we raise instead of returning a partial (unparseable) body.
    private static async Task<string> ReadStreamedText(
        HttpResponseMessage response, int maxTokens, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var text = new StringBuilder();
        string? stopReason = null;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line["data:".Length..].Trim();
            if (data.Length == 0)
                continue;

            using var evt = JsonDocument.Parse(data);
            var root = evt.RootElement;
            if (!root.TryGetProperty("type", out var typeProperty))
                continue;

            switch (typeProperty.GetString())
            {
                case "content_block_delta"
                    when root.TryGetProperty("delta", out var delta) &&
                         delta.TryGetProperty("type", out var deltaType) &&
                         deltaType.GetString() == "text_delta" &&
                         delta.TryGetProperty("text", out var chunk):
                    text.Append(chunk.GetString());
                    break;

                case "message_delta"
                    when root.TryGetProperty("delta", out var messageDelta) &&
                         messageDelta.TryGetProperty("stop_reason", out var reason) &&
                         reason.ValueKind == JsonValueKind.String:
                    stopReason = reason.GetString();
                    break;

                case "error":
                    throw StreamError(root);

                case "message_stop":
                    return Finish(text, stopReason, maxTokens);
            }
        }

        return Finish(text, stopReason, maxTokens);
    }

    private static string Finish(StringBuilder text, string? stopReason, int maxTokens)
    {
        if (stopReason == "max_tokens")
            throw new ResponseTruncatedException(
                $"Claude response was truncated at the {maxTokens}-token output limit " +
                "(stop_reason=max_tokens); the response is incomplete. Increase the request's " +
                "max_tokens budget.");

        return text.ToString();
    }

    private static Exception StreamError(JsonElement root)
    {
        var body = root.GetRawText();
        var type = root.TryGetProperty("error", out var error) && error.TryGetProperty("type", out var t)
            ? t.GetString()
            : null;

        if (type is "rate_limit_error" or "overloaded_error")
            return new RateLimitException($"Anthropic rate limited (stream): {body}");

        return new HttpRequestException($"Anthropic stream error: {body}");
    }

    private static Exception ToException(HttpResponseMessage response, string body)
    {
        var status = response.StatusCode;
        if (status == HttpStatusCode.TooManyRequests || (int)status == 529 || IsRateLimitBody(body))
        {
            return new RateLimitException(
                $"Anthropic rate limited: {(int)status} ({status}): {body}",
                response.Headers.RetryAfter?.Delta);
        }

        return new HttpRequestException(
            $"Anthropic request failed: {(int)status} ({status}): {body}", inner: null, status);
    }

    private static bool IsRateLimitBody(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("type", out var type))
            {
                var value = type.GetString();
                return value is "rate_limit_error" or "overloaded_error";
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body: fall back to status-code classification.
        }
        return false;
    }

    private static IReadOnlyList<EntityMention> MapMentions(List<EntityDto>? entities) =>
        (entities ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => new EntityMention(e.Name!, e.Description ?? string.Empty, e.Scale ?? string.Empty, e.Aliases ?? []))
            .ToList();

    private sealed class FragmentDto
    {
        public List<EntityDto>? Characters { get; set; }
        public List<EntityDto>? Settings { get; set; }
    }

    private sealed class EntityDto
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Scale { get; set; }
        public List<string>? Aliases { get; set; }
    }

    private sealed class ArtStyleDto
    {
        public string? ArtStyle { get; set; }
    }

    private sealed class ChapterNotesDto
    {
        public List<string>? CharactersPresent { get; set; }
        public List<string>? KeyEvents { get; set; }
        public List<string>? CharacterBeats { get; set; }
        public List<string>? SetupsAndPayoffs { get; set; }
        public List<string>? PossibleInconsistencies { get; set; }
        public List<string>? FactualClaims { get; set; }
        public List<string>? NotableStrengths { get; set; }
        public List<string>? NotableWeaknesses { get; set; }
        public List<string>? LineEditCandidates { get; set; }
        public List<string>? ProseObservations { get; set; }
        public List<string>? DialogueObservations { get; set; }
        public List<string>? SceneBeats { get; set; }
        public List<string>? SummarizedMoments { get; set; }
        public List<string>? ThinSpots { get; set; }
        public List<string>? OffPageEvents { get; set; }
        public string? RunningState { get; set; }
    }

    private sealed class ReconciliationDto
    {
        public List<MatchDto>? Matches { get; set; }
    }

    private sealed class MatchDto
    {
        public int Index { get; set; }
        public string? ThreadId { get; set; }
    }

    private sealed class ExpansionDto
    {
        public List<SceneAdditionDto>? ScenesToAdd { get; set; }
        public List<ShowTellDto>? ShowVsTell { get; set; }
        public int TargetLow { get; set; }
        public int TargetHigh { get; set; }
    }

    private sealed class SceneAdditionDto
    {
        public string? Kind { get; set; }
        public string? Location { get; set; }
        public string? Missing { get; set; }
        public string? Why { get; set; }
        public string? Priority { get; set; }
    }

    private sealed class ShowTellDto
    {
        public string? Chapter { get; set; }
        public string? Excerpt { get; set; }
        public string? Direction { get; set; }
        public string? Suggestion { get; set; }
    }

    private sealed class SeriesContinuityDto
    {
        public List<SeriesBreakDto>? Breaks { get; set; }
        public List<CarriedThreadDto>? CarriedThreads { get; set; }
    }

    private sealed class SeriesBreakDto
    {
        public string? Chapter { get; set; }
        public string? Established { get; set; }
        public string? Contradiction { get; set; }
    }

    private sealed class CarriedThreadDto
    {
        public string? Established { get; set; }
        public string? Status { get; set; }
    }

    private sealed class BookAnalysisDto
    {
        public string? Logline { get; set; }
        public string? Synopsis { get; set; }
        public List<CharacterProfileDto>? Characters { get; set; }
        public List<string>? Themes { get; set; }

        // Findings are read as the domain type, not a DTO: its converter already tolerates both the
        // {"chapter","detail"} object asked for and a bare string (which is what a model that skims
        // the instruction returns, and what every report stored before citations existed contains).
        public List<ReportFinding>?PlotHoles { get; set; }
        public List<ReportFinding>?Inconsistencies { get; set; }
        public List<ReportFinding>?Inaccuracies { get; set; }
        public List<ReportFinding>?PacingNotes { get; set; }
        public List<ReviewDto>? Reviews { get; set; }
        public List<RevisionItemDto>? RevisionPlan { get; set; }
        public List<string>? ComparableTitles { get; set; }
        public string? MarketPositioning { get; set; }
        public string? Genre { get; set; }
        public string? WordCountRange { get; set; }
        public List<ChapterTensionDto>? PacingCurve { get; set; }
        public VerdictDto? Verdict { get; set; }
    }

    private sealed class ChapterTensionDto
    {
        public string? Chapter { get; set; }
        public double Tension { get; set; }
    }

    private sealed class VerdictDto
    {
        public List<string>? Strengths { get; set; }
        public List<string>? Problems { get; set; }
        public string? MostNeeds { get; set; }
        public string? Standing { get; set; }
        public string? Reading { get; set; }
    }

    private sealed class CraftReportDto
    {
        public List<LineEditDto>? LineEdits { get; set; }
        public List<ProseNoteDto>? ProseNotes { get; set; }
        public List<DialogueNoteDto>? DialogueNotes { get; set; }
        public List<SceneBeatDto>? SceneBreakdown { get; set; }
    }

    private sealed class LineEditDto
    {
        public string? Chapter { get; set; }
        public string? Excerpt { get; set; }
        public string? Suggestion { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class ProseNoteDto
    {
        public string? Chapter { get; set; }
        public string? Pattern { get; set; }
        public string? Evidence { get; set; }
        public string? Fix { get; set; }
    }

    private sealed class DialogueNoteDto
    {
        public string? Chapter { get; set; }
        public string? Speakers { get; set; }
        public string? Observation { get; set; }
        public string? Fix { get; set; }
    }

    private sealed class SceneBeatDto
    {
        public string? Chapter { get; set; }
        public string? Scene { get; set; }
        public string? Purpose { get; set; }
        public string? Verdict { get; set; }
    }

    private sealed class CharacterProfileDto
    {
        public string? Name { get; set; }
        public string? Role { get; set; }
        public string? ArcSummary { get; set; }
        public List<string>? Relationships { get; set; }
    }

    // Perspective and Recommendation are the old names, still read so that a reply generated from a
    // prompt that has not been reworded yet still maps rather than silently coming back empty.
    private sealed class SectionDto
    {
        public string? Summary { get; set; }
        public List<SectionFindingDto>? Findings { get; set; }
    }

    private sealed class SectionFindingDto
    {
        public string? Chapter { get; set; }
        public string? Detail { get; set; }
    }

    private sealed class SynthesisDto
    {
        public List<RevisionItemDto>? RevisionPlan { get; set; }
        public VerdictDto? Verdict { get; set; }
    }

    private sealed class ReviewDto
    {
        public string? PersonaId { get; set; }
        public string? Perspective { get; set; }
        public string? Verdict { get; set; }
        public string? Assessment { get; set; }
        public List<string>? Strengths { get; set; }
        public List<string>? Weaknesses { get; set; }
        public string? Condition { get; set; }
        public string? Recommendation { get; set; }
    }

    private sealed class RevisionItemDto
    {
        public string? Priority { get; set; }
        public string? Chapter { get; set; }
        public string? Issue { get; set; }
        public string? SuggestedFix { get; set; }
    }

    private sealed class VoiceConsistencyDto
    {
        public string? DominantVoice { get; set; }
        public List<VoiceDivergenceDto>? Divergences { get; set; }
    }

    private sealed class VoiceDivergenceDto
    {
        public string? Chapter { get; set; }
        public string? Excerpt { get; set; }
        public string? Divergence { get; set; }
        public string? Suggestion { get; set; }
    }
}
