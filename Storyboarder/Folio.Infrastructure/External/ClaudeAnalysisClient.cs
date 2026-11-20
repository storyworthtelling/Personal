using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Folio.Domain.Analysis;
using Folio.Domain.Markup;
using Folio.Domain.Reading;
using Folio.Domain.Series;
using Folio.Domain.Scenes;
using Folio.Domain.External;
using Folio.Domain.Health;
using Folio.Domain.Model;
using Folio.Domain.Pipeline;
using Folio.Domain.Text;
using Folio.Infrastructure.Configuration;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Pipeline;

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
        "illustrator’s reference sheets, so any character or place you omit gets drawn from scratch and " +
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
        "You are an art director choosing the visual style for illustrated evidence images of a novel’s " +
        "scenes. From the prose sample and the cast below, infer the book’s genre, tone, period and " +
        "setting, then describe ONE cohesive illustration style for the WHOLE book — medium, rendering, " +
        "linework, colour palette, lighting and mood — in a single vivid sentence an illustrator could " +
        "follow consistently. Respond with ONLY a JSON object: {\"artStyle\":string}. No prose outside the JSON.";

    // One detected scene, judged on its own (release A3–A4). Nudge never write: the verdict and the notes
    // point at problems and never supply the author's sentences.
    private const string SceneJudgementInstruction =
        "You are judging ONE scene of a manuscript as a developmental editor. You are given the scene’s own " +
        "text, the story so far when it begins, the book’s dominant voice, and the cast and settings it names. " +
        "Respond with ONLY a JSON object: {\"accomplishes\":string,\"call\":\"Keep|Tighten|Rewrite|Cut\"," +
        "\"tension\":number,\"verdict\":string,\"inconsistencies\":[{\"kind\":\"Voice|Tense|Characterisation|Setting\"," +
        "\"detail\":string,\"excerpt\":string,\"severity\":\"Must|Should|Consider\"}]}. accomplishes = what this scene does for the plot and for the " +
        "characters, in one or two sentences. call = Keep (it works), Tighten (it works but runs long or repeats), " +
        "Rewrite (it does not yet do its job), or Cut (the book loses nothing without it). tension = how much " +
        "pressure the scene carries, from 0 (calm) to 1 (peak). verdict = one or two sentences on why the call. " +
        "inconsistencies = places where the scene’s voice drifts from the book’s, its tense slips, a character " +
        "acts or sounds unlike the story so far without cause, or a setting contradicts what was established; " +
        "an empty list when there are none. excerpt = a short verbatim quote (under fifteen words) from THIS " +
        "scene where the inconsistency is, so the note can be written on that line. severity = how much it matters: \"Must\" (the book does not work until it is fixed), \"Should\" (it weakens the book), or \"Consider\" (a refinement). You diagnose and point: " +
        "never write replacement prose, never rewrite a sentence, never supply the author’s words.";

    private const string SceneImageInstruction =
        "You are a visual-development artist illustrating ONE scene of a novel for a fidelity check: the " +
        "image will be placed beside the prose to test whether the scene’s key beat lands visually AS " +
        "WRITTEN. From the scene text and the cast/settings below, choose the scene’s EMOTIONAL PEAK — the " +
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
        "SO FAR with this excerpt’s plot developments, the current state of each character, open questions, " +
        "and planted-but-unpaid setups — a few tight paragraphs, cumulative, so the next excerpt inherits it. " +
        "characterBeats = decisions, " +
        "changes, or motivation shifts; setupsAndPayoffs = planted elements or promises (note if paid " +
        "off here); factualClaims = checkable real-world statements that may need fact-checking; " +
        "possibleInconsistencies = anything that may contradict earlier text (names, timeline, physical " +
        "details, world rules). " +
        // The report writes each of these onto the manuscript page it is about, and by consolidation
        // time only these notes survive — so a passage not quoted now can never be found again.
        "For possibleInconsistencies, notableWeaknesses, thinSpots and offPageEvents, include a SHORT " +
        "verbatim phrase from the excerpt (under fifteen words) in double quotes inside the note, " +
        "pointing at the exact place it concerns. " +
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
        "\"pacingCurve\":[{\"chapter\":string,\"tension\":number,\"driver\":string}],\"reviews\":" +
        "[{\"perspective\":string,\"assessment\":string,\"strengths\":[string],\"weaknesses\":[string]," +
        "\"recommendation\":string}],\"revisionPlan\":[{\"priority\":string,\"chapter\":string," +
        "\"issue\":string,\"suggestedFix\":string}]," +
        "\"verdict\":{\"standing\":string,\"reading\":string,\"strengths\":[string]," +
        "\"problems\":[string],\"mostNeeds\":string}}. " +
        // Every finding has to be findable: an observation the author can't locate in the manuscript
        // costs them a re-read to act on. The chapter is already on the notes, so it only has to be
        // carried through rather than worked out.
        "FINDING is an object {\"chapter\":string,\"detail\":string,\"excerpt\":string,\"severity\":\"Must|Should|Consider\"}: copy \"chapter\" from the note the " +
        "observation came from (\"Chapter 7\", \"Chapters 7–8\"), naming the EARLIEST chapter involved " +
        "when an observation spans several, and use \"\" only when the notes carry no chapter at all. " +
        // The excerpt is what lets the finding be written on the page it concerns.
        "\"excerpt\": when the note you drew the finding from quotes a phrase, copy that phrase VERBATIM " +
        "(a short one, under fifteen words) so the finding can be written on that line of the manuscript; " +
        "use \"\" when the finding is about the book as a whole or no phrase was quoted." +
        " severity = how much it matters: \"Must\" (the book does not work until it is fixed), \"Should\" (it weakens the book), or \"Consider\" (a refinement). " +
        "Set \"chapter\" on each revisionPlan item the same way. " +
        "genre: infer the book’s genre. wordCountRange: the word-count range publishers expect for that " +
        "genre (e.g. \"90,000–120,000\"). " +
        "pacingCurve: one entry PER CHAPTER in reading order with tension from 0.0 (calm) to 1.0 (peak), and " +
        "\"driver\": three to six words on what drives or drains that chapter’s tension (\"the storm breaks\", " +
        "\"backstory stalls the chase\"), printed over its point on the chart. The curve is what makes " +
        "a saggy stretch visible; copy the chapter label from the notes. " +
        "Provide AT LEAST FOUR reviews from DISTINCT, fair perspectives — a DEVELOPMENTAL EDITOR (structure, " +
        "arcs, stakes), the TARGET-GENRE READER (where they’re gripped, bored, confused, moved), the SKEPTIC " +
        "(the harshest fair read), and an AGENT & ACQUIRING EDITOR (is it sellable, is it ready, where it " +
        "sits in the market, and a readiness read: early draft / revision / near-submittable) — differing " +
        "but honest, each with real strengths and weaknesses. " +
        "Order revisionPlan HIGHEST-IMPACT FIRST and set priority to \"Must\", \"Should\", or \"Consider\". " +
        "verdict: the candid state of the draft — biggest strengths, biggest problems, and the single thing " +
        "this draft most needs before the next revision. Use [] when none. No prose outside the JSON.";

    // The scene-by-scene map is no longer asked for here: each detected scene is judged on its own
    // (JudgeScene, release A3). A reply that still carries a sceneBreakdown is parsed, for stored reports.
    private const string CraftConsolidationInstruction =
        "You are a line editor and craft critic producing the CRAFT half of a REVISION REPORT for an " +
        "author preparing a novel for publication. You are given per-chunk notes gathered across the " +
        "whole manuscript in reading order; each carries the chapter it was taken from, and " +
        "lineEditCandidates/proseObservations/dialogueObservations are the raw material for " +
        "your three sections. Consolidate them into the patterns and edits that matter for the WHOLE " +
        "book — merge duplicates, drop the trivial, keep the author’s own words when quoting. Respond " +
        "with ONLY a JSON object: " +
        "{\"lineEdits\":[{\"chapter\":string,\"excerpt\":string,\"suggestion\":string,\"reason\":string,\"kind\":string,\"severity\":\"Must|Should|Consider\"}]," +
        "\"proseNotes\":[{\"chapter\":string,\"pattern\":string,\"evidence\":string,\"fix\":string,\"severity\":\"Must|Should|Consider\"}]," +
        "\"dialogueNotes\":[{\"chapter\":string,\"speakers\":string,\"observation\":string,\"fix\":string,\"excerpt\":string,\"severity\":\"Must|Should|Consider\"}]}. " +
        "lineEdits: specific sentences to cut, tighten, or rewrite — \"excerpt\" quoted verbatim from " +
        "the notes so the author can find it, \"suggestion\" the actual replacement or cut, \"reason\" " +
        "one short clause, \"kind\" the ONE word that names what is actually wrong with the sentence: " +
        "\"FilterWord\" (she felt/he saw/I thought distancing the reader), \"Redundant\" (says twice what " +
        "one sentence already said), \"WeakWord\" (a vague or imprecise word choice), \"Cliche\" (a stock " +
        "phrase or transition), \"Grammar\" (a malformed or ungrammatical sentence), \"Digression\" (an " +
        "aside that adds nothing), \"Tone\" (a register slip), or \"Other\" if none fit. AT MOST 40, " +
        "chosen for impact. " +
        "proseNotes: recurring prose-style and voice patterns — repetition, filter words, verbal tics, " +
        "monotonous sentence rhythm, overused constructions, POV slips — with \"evidence\" quoting a " +
        "real example. AT MOST 20. " +
        "dialogueNotes: whether the characters sound distinct from one another, on-the-nose exchanges, " +
        "and exposition dumped into speech. \"excerpt\": when the note is about a SPECIFIC exchange, " +
        "quote it verbatim from the notes; leave \"\" for an aggregate observation with no single " +
        "exchange to point at (e.g. \"all three sound identical\"). AT MOST 20. " +
        "Every \"chapter\" is copied from the note the item came from; \"\" only when the notes carry " +
        "none. Every item has a severity: \"Must\" (the book does not work until it is fixed), \"Should\" (it weakens the book), or \"Consider\" (a refinement). Use [] when none. No prose outside the JSON.";

    private const string VoiceConsistencyInstruction =
        "You are a developmental editor assessing a manuscript’s VOICE CONSISTENCY for an author preparing " +
        "it for publication. You are given a representative prose sample from each chapter, each tagged " +
        "with its chapter. First infer the book’s DOMINANT authorial voice — its register, diction, " +
        "sentence cadence, tone, and narrative stance — from the body of samples. Then flag the passages " +
        "whose prose DRIFTS from that dominant voice: a chapter that slips into a different register, an " +
        "out-of-place tonal shift, diction or rhythm unlike the rest. Judge consistency of VOICE only — " +
        "not quality, plot, or subject matter. A deliberately distinct voice (a different POV character’s " +
        "chapter, a letter or document embedded in the text) is NOT a defect, so flag only drift that " +
        "reads as unintentional. Respond with ONLY a JSON object: " +
        "{\"dominantVoice\":string,\"divergences\":[{\"chapter\":string,\"excerpt\":string," +
        "\"divergence\":string,\"suggestion\":string,\"severity\":\"Must|Should|Consider\"}]}. dominantVoice: two or three sentences " +
        "characterizing the book’s established voice. excerpt: quote the divergent prose VERBATIM from the " +
        "sample so the author can find it. divergence: how it departs from the dominant voice. suggestion: " +
        "how to bring it into line. severity = how much it matters: \"Must\" (the book does not work until it is fixed), \"Should\" (it weakens the book), or \"Consider\" (a refinement). Report AT MOST 20 divergences, the clearest ones, and use [] when the " +
        "voice is consistent throughout. No prose outside the JSON.";

    private const string ReconciliationInstruction =
        "You are reconciling a manuscript’s revision findings ACROSS DRAFTS. You are given the PRIOR draft’s " +
        "findings (each with a stable id, category, chapter, and text) and the CURRENT draft’s findings (each " +
        "with a numeric index). For each CURRENT finding, decide whether it is the SAME underlying issue as one " +
        "of the prior findings — the same plot hole, inconsistency, inaccuracy, or pacing problem — even if the " +
        "wording, phrasing, or chapter citation has moved between drafts, or whether it is a genuinely NEW " +
        "issue. Match CONSERVATIVELY: only when it is really the same issue, not merely a similar topic or the " +
        "same chapter. Respond with ONLY a JSON object: {\"matches\":[{\"index\":int,\"threadId\":string|null}]}. " +
        "index: the current finding’s index. threadId: the id of the prior finding it is the same issue as, or " +
        "null when it is new. Include exactly one entry per current finding, and never map two current findings " +
        "to the same prior id. No prose outside the JSON.";

    private const string ExpansionInstruction =
        "You are a developmental editor helping an author whose early drafts run SHORT. Your job here is " +
        "ADDITIVE: find what the book is missing and where it could grow. You are given the per-chunk notes " +
        "(each chunk’s summarized moments, off-page events, thin spots, character beats, and setups), a " +
        "running story-state, the manuscript’s actual WORD COUNT, and its GENRE. You NUDGE, you never write: " +
        "every suggestion is a POINTER the author acts on themselves — never drafted prose, never an example " +
        "scene, never sample lines. Respond with ONLY a JSON object: " +
        "{\"scenesToAdd\":[{\"kind\":string,\"location\":string,\"missing\":string,\"why\":string," +
        "\"priority\":string,\"excerpt\":string}],\"showVsTell\":[{\"chapter\":string,\"excerpt\":string,\"direction\":string," +
        "\"suggestion\":string,\"severity\":\"Must|Should|Consider\"}],\"targetLow\":number,\"targetHigh\":number}. " +
        "scenesToAdd = the scenes/beats to ADD, ranked most-impactful first. kind is one of " +
        "\"MissingBeat\" (a turn that happens off-page), \"CharacterDeepening\", \"TensionStakes\", or " +
        "\"ThemeDeepening\". location says where it belongs (\"Chapter 4\", \"after Chapter 7\"). missing = " +
        "what the reader never gets to see. excerpt = a short phrase quoted VERBATIM from the notes at the " +
        "passage the new scene belongs beside (\"\" if none). why = the impact of adding it. priority is one of \"Must\", " +
        "\"Should\", \"Consider\". showVsTell = show-vs-tell notes on BOTH ends: direction \"Dramatize\" for a " +
        "SUMMARIZED passage that could become a shown scene (the main lever for adding words) or \"Tighten\" " +
        "for an OVER-shown passage that runs long; quote the excerpt VERBATIM and give a pointer, not a " +
        "rewrite; each showVsTell note has a severity: \"Must\" (the book does not work until it is fixed), \"Should\" (it weakens the book), or \"Consider\" (a refinement). targetLow/targetHigh = the typical published word-count range for this genre (integers; use " +
        "0/0 if you can’t judge it). Give AT MOST 15 scenesToAdd and 15 showVsTell, the most significant, and " +
        "use [] when there are none. No prose outside the JSON.";

    private const string SeriesContinuityInstruction =
        "You are a continuity editor checking a novel that is a LATER BOOK in a series against the canon its " +
        "PRIOR book established. You are given the prior book’s canon — its cast and settings with the " +
        "descriptions the prior book established, where its story stood at the end, and the unresolved problems " +
        "it left open — and this book’s per-chunk notes and running story state. Do TWO things. (1) Find " +
        "CONTRADICTIONS: places where this book contradicts what the prior book established — a character " +
        "described or behaving inconsistently with their canon, a place / world rule / timeline that clashes, a " +
        "fact that cannot be true if the prior book’s was. Be specific and CONSERVATIVE: a deliberate " +
        "development, reveal, or change over time is NOT a contradiction — flag only genuine clashes. (2) For " +
        "each of the prior book’s OPEN THREADS, judge whether this book ADDRESSES it (resolves or meaningfully " +
        "advances it), IGNORES it (never touches it), or CONTRADICTS it. You NUDGE, you never rewrite: point at " +
        "the problem, never draft the fix. Respond with ONLY a JSON object: " +
        "{\"breaks\":[{\"chapter\":string,\"established\":string,\"contradiction\":string,\"excerpt\":string,\"severity\":\"Must|Should|Consider\"}]," +
        "\"carriedThreads\":[{\"established\":string,\"status\":string}]}. breaks: chapter = where in THIS book " +
        "(\"Chapter 4\", \"\" if unknown); established = what the prior book set; contradiction = what this book " +
        "says instead; excerpt = a short phrase from THIS book’s notes, verbatim, where the clash is (\"\" if none); severity = how much it matters: \"Must\" (the book does not work until it is fixed), \"Should\" (it weakens the book), or \"Consider\" (a refinement). carriedThreads: established = the prior open thread verbatim; status = one of " +
        "\"Addressed\", \"Ignored\", \"Contradicted\". Give one carriedThreads entry per open thread provided. " +
        "Use [] when there are no breaks. No prose outside the JSON.";

    // Appended when the book is checked against the series bible (R22–R24).
    private const string SeriesBibleCheckInstruction =
        " You are given the SERIES BIBLE of the books written before this one, in place of the prior book’s canon: each " +
        "entry with the book that established it, the story time it holds as of, and that book’s own words. Where this " +
        "book and the bible disagree, the EARLIER-WRITTEN book is canon and this book is the one to flag, even when this " +
        "book is set earlier in story time. Read the bible against where this book sits in story time: a character " +
        "younger, a place not yet built or a thread not yet opened is no clash in a book set before it. Check four things " +
        "and give each break a kind: \"Contradicted fact\" (a fact this book cannot share with the bible), \"Character " +
        "drift\" (a character who acts, speaks or is described unlike the bible’s without the story earning it), \"Dropped " +
        "thread\" (an open thread this book should take up and does not, or abandons), \"Timeline error\" (an order, age, " +
        "date or interval that cannot be true). For each break also give \"book\" (the title of the earlier book it " +
        "contradicts) and \"quote\" (that book’s own words for what it established, copied from the bible’s quoted " +
        "passage, or \"\" when the bible has none).";

    private readonly HttpClient httpClient;
    private readonly ClaudeOptions options;
    private readonly ICredentialHealthSink? healthSink;
    private readonly ILogger<ClaudeAnalysisClient>? logger;

    public ClaudeAnalysisClient(
        HttpClient httpClient, IOptions<ClaudeOptions> options, ICredentialHealthSink? healthSink = null,
        ILogger<ClaudeAnalysisClient>? logger = null)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.healthSink = healthSink;
        this.logger = logger;
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

    // The image-prompt request's content: the one place it is built, for the direct call and the batch.
    private static string SceneImageContent(
        string sceneText, DesignBible bible, string artStyle, IReadOnlyList<string> avoidMoments)
    {
        // Only the cast and settings this scene names: see DesignBible.ForScene.
        var bibleJson = JsonSerializer.Serialize(bible.ForScene(sceneText), JsonResponse.CaseInsensitive);
        var avoid = avoidMoments.Count == 0
            ? "(none)"
            : string.Join("\n", avoidMoments.Select(m => $"- {m}"));
        return
            $"ART STYLE: {artStyle}\n\n" +
            $"CAST & SETTINGS (JSON):\n{bibleJson}\n\n" +
            $"MOMENTS TO AVOID:\n{avoid}\n\n" +
            $"SCENE:\n{sceneText}";
    }

    // ---- The batch tier (owner's ruling, R39) ----------------------------------------------------------
    // The same requests as the direct calls (same instruction, same content, same model and effort), sent
    // together at half the cost of every token and read back by custom id, never by position.

    private const string BatchesUrl = "https://api.anthropic.com/v1/messages/batches";

    public Task<string?> SubmitSceneJudgements(
        IReadOnlyList<SceneToJudge> scenes, DesignBible bible, CancellationToken cancellationToken) =>
        SubmitBatch(
            scenes.Select(scene => (SceneKey(scene.Number), MessageParams(SceneJudgementInstruction, SceneJudgementContent(scene, bible)))).ToList(),
            nameof(JudgeScene), cancellationToken);

    public Task<string?> SubmitSceneImagePrompts(
        IReadOnlyList<SceneImageRequest> scenes, DesignBible bible, string artStyle, CancellationToken cancellationToken) =>
        SubmitBatch(
            scenes.Select(scene => (SceneKey(scene.Number), MessageParams(SceneImageInstruction, SceneImageContent(scene.Text, bible, artStyle, [])))).ToList(),
            nameof(DescribeSceneImage), cancellationToken);

    public Task<string?> SubmitReads(
        IReadOnlyList<Persona> personas, string manuscript, ReadingFrame frame, CancellationToken cancellationToken) =>
        SubmitBatch(
            personas.Select(persona =>
            {
                var pass = PassRequest(persona, manuscript, frame, persona.Kind == PersonaKind.Reader ? ReaderFormat : SectionFormat(manuscript));
                return (PassKey(persona.Id), (object)new
                {
                    model = pass.Model,
                    max_tokens = pass.MaxTokens,
                    output_config = new { effort = options.PassEffort },
                    system = pass.System,
                    messages = new[] { new { role = "user", content = pass.User } },
                });
            }).ToList(),
            "ReadPass", cancellationToken);

    public async Task<IReadOnlyList<PassBatchItem>> ReadResults(
        string batchId, IReadOnlyList<Persona> personas, CancellationToken cancellationToken)
    {
        var byKey = personas.ToDictionary(persona => PassKey(persona.Id));
        var items = new List<PassBatchItem>();
        foreach (var (key, text, failure) in await BatchTexts(batchId, "ReadPass", cancellationToken))
        {
            if (!byKey.TryGetValue(key, out var persona))
                continue;
            if (text is null)
            {
                items.Add(new PassBatchItem(persona.Id, null, failure));
                continue;
            }
            try
            {
                var json = persona.Kind == PersonaKind.Reader
                    ? JsonSerializer.Serialize(ReadReaderReply(persona, text), JsonResponse.CaseInsensitive)
                    : JsonSerializer.Serialize(ReadSectionReply(persona, text), JsonResponse.CaseInsensitive);
                items.Add(new PassBatchItem(persona.Id, json, null));
            }
            catch (InvalidOperationException incomplete)
            {
                items.Add(new PassBatchItem(persona.Id, null, incomplete.Message));
            }
        }
        return items;
    }

    private static string PassKey(string personaId) => $"pass-{personaId}";

    // An ordinary request's params: its constant instruction, cached, then the content.
    private object MessageParams(string instruction, string content) => new
    {
        model = options.Model,
        max_tokens = options.MaxTokens,
        output_config = new { effort = options.Effort },
        system = new object[] { new { type = "text", text = instruction, cache_control = new { type = "ephemeral" } } },
        messages = new[] { new { role = "user", content } },
    };

    public async Task<bool> BatchEnded(string batchId, CancellationToken cancellationToken)
    {
        using var batch = await GetBatch(batchId, cancellationToken);
        return batch.RootElement.TryGetProperty("processing_status", out var status) && status.GetString() == "ended";
    }

    public async Task<IReadOnlyList<BatchItem<SceneJudgement>>> SceneJudgementResults(
        string batchId, IReadOnlyDictionary<int, string> chapters, CancellationToken cancellationToken)
    {
        var items = new List<BatchItem<SceneJudgement>>();
        foreach (var (key, text, failure) in await BatchTexts(batchId, nameof(JudgeScene), cancellationToken))
        {
            if (SceneNumber(key) is not { } number)
                continue;
            if (text is null)
            {
                items.Add(new BatchItem<SceneJudgement>(number, null, failure));
                continue;
            }
            try
            {
                items.Add(new BatchItem<SceneJudgement>(
                    number, ReadSceneJudgement(number, chapters.TryGetValue(number, out var chapter) ? chapter : string.Empty, text), null));
            }
            catch (InvalidOperationException unreadable)
            {
                items.Add(new BatchItem<SceneJudgement>(number, null, unreadable.Message));
            }
        }
        return items;
    }

    public async Task<IReadOnlyList<BatchItem<string>>> SceneImagePromptResults(string batchId, CancellationToken cancellationToken) =>
        (await BatchTexts(batchId, nameof(DescribeSceneImage), cancellationToken))
            .Where(item => SceneNumber(item.Key) is not null)
            .Select(item => new BatchItem<string>(SceneNumber(item.Key)!.Value, item.Text?.Trim(), item.Failure))
            .ToList();

    private static int? SceneNumber(string key) =>
        key.StartsWith("scene-", StringComparison.Ordinal) && int.TryParse(key["scene-".Length..], out var number) ? number : null;

    private static string SceneKey(int number) => $"scene-{number}";

    private async Task<string?> SubmitBatch(
        IReadOnlyList<(string CustomId, object Params)> items, string call,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return null;

        var payload = new
        {
            requests = items.Select(item => new { custom_id = item.CustomId, @params = item.Params }).ToList(),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BatchesUrl);
        request.Headers.Add("x-api-key", options.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (AuthFailure.IsHttpCredentialRejection((int)response.StatusCode, body))
                healthSink?.MarkFailing(CredentialIds.Claude, $"{(int)response.StatusCode} {response.StatusCode}");
            logger?.LogWarning("Claude batch for {Call} ({Count} requests) was refused: {Status}", call, items.Count, (int)response.StatusCode);
            throw ToException(response, body);
        }

        healthSink?.MarkOk(CredentialIds.Claude);
        using var created = JsonDocument.Parse(body);
        var id = created.RootElement.GetProperty("id").GetString();
        logger?.LogInformation("Claude batch {BatchId} submitted for {Call}: {Count} requests at the batch rate", id, call, items.Count);
        return id;
    }

    private async Task<JsonDocument> GetBatch(string batchId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BatchesUrl}/{Uri.EscapeDataString(batchId)}");
        request.Headers.Add("x-api-key", options.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw ToException(response, body);
        return JsonDocument.Parse(body);
    }

    // Each result line of a finished batch: the scene it belongs to (by custom id), the reply's text when
    // it succeeded, or why there is none. Every item is logged with its tokens, as a direct call is.
    private async Task<IReadOnlyList<(string Key, string? Text, string? Failure)>> BatchTexts(
        string batchId, string call, CancellationToken cancellationToken)
    {
        string resultsUrl;
        using (var batch = await GetBatch(batchId, cancellationToken))
        {
            resultsUrl = batch.RootElement.TryGetProperty("results_url", out var url) && url.GetString() is { Length: > 0 } found
                ? found
                : throw new InvalidOperationException($"Batch {batchId} has no results yet.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, resultsUrl);
        request.Headers.Add("x-api-key", options.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw ToException(response, body);

        var items = new List<(string, string?, string?)>();
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var item = JsonDocument.Parse(line);
            var customId = item.RootElement.GetProperty("custom_id").GetString() ?? string.Empty;

            var result = item.RootElement.GetProperty("result");
            var type = result.GetProperty("type").GetString();
            if (type == "succeeded")
            {
                var message = result.GetProperty("message");
                var text = string.Concat(message.GetProperty("content").EnumerateArray()
                    .Where(block => block.TryGetProperty("type", out var kind) && kind.GetString() == "text")
                    .Select(block => block.GetProperty("text").GetString()));
                var usage = message.TryGetProperty("usage", out var u) ? u : default;
                logger?.LogInformation(
                    "Claude batch {BatchId} item {CustomId} ({Call}) succeeded: input={Input} cacheWrite={CacheWrite} cacheRead={CacheRead} output={Output}",
                    batchId, customId, call, Tokens(usage, "input_tokens"), Tokens(usage, "cache_creation_input_tokens"),
                    Tokens(usage, "cache_read_input_tokens"), Tokens(usage, "output_tokens"));
                items.Add((customId, text, null));
            }
            else
            {
                var why = type == "errored" && result.TryGetProperty("error", out var error)
                    ? $"errored: {(error.TryGetProperty("message", out var m) ? m.GetString() : error.ToString())}"
                    : type ?? "unknown";
                logger?.LogWarning("Claude batch {BatchId} item {CustomId} ({Call}) did not succeed: {Why}", batchId, customId, call, why);
                items.Add((customId, null, why));
            }
        }
        return items;
    }

    private static int Tokens(JsonElement usage, string name) =>
        usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(name, out var value) && value.TryGetInt32(out var tokens) ? tokens : 0;

    public async Task<SceneJudgement> JudgeScene(
        SceneToJudge scene, DesignBible bible, CancellationToken cancellationToken)
    {
        var reply = await SendMessage(SceneJudgementInstruction, SceneJudgementContent(scene, bible), cancellationToken);
        return ReadSceneJudgement(scene.Number, scene.Chapter, reply);
    }

    // The scene-judgement request's content: the one place it is built, for the direct call and the batch.
    private static string SceneJudgementContent(SceneToJudge scene, DesignBible bible)
    {
        var cast = JsonSerializer.Serialize(bible.ForScene(scene.Text), JsonResponse.CaseInsensitive);
        return
            $"BOOK'S DOMINANT VOICE:\n{(string.IsNullOrWhiteSpace(scene.DominantVoice) ? "(not yet described)" : scene.DominantVoice)}\n\n" +
            $"STORY SO FAR:\n{(string.IsNullOrWhiteSpace(scene.StorySoFar) ? "(this is the opening)" : scene.StorySoFar)}\n\n" +
            $"CAST & SETTINGS THIS SCENE NAMES (JSON):\n{cast}\n\n" +
            $"SCENE {scene.Number} ({scene.Chapter}):\n{scene.Text}";
    }

    // A scene judgement read from the model's reply; the one place it is parsed, for both paths.
    private static SceneJudgement ReadSceneJudgement(int number, string chapter, string reply)
    {
        var scene = new { Number = number, Chapter = chapter };
        var json = JsonResponse.ExtractObject(reply)
            ?? throw new InvalidOperationException(
                $"Scene {scene.Number}’s judgement came back with nothing readable as JSON; the pass is retried.");

        var dto = JsonResponse.Deserialize<SceneJudgementDto>(json);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Accomplishes))
            throw new InvalidOperationException($"Scene {scene.Number}’s judgement did not say what the scene accomplishes.");
        if (!Enum.TryParse<SceneCall>(dto.Call?.Trim(), ignoreCase: true, out var call) || !Enum.IsDefined(call))
            throw new InvalidOperationException($"Scene {scene.Number}’s judgement gave no keep/tighten/rewrite/cut call (got '{dto.Call}').");

        var inconsistencies = (dto.Inconsistencies ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Detail))
            .Select(item => (Ok: Enum.TryParse<SceneInconsistencyKind>(item.Kind?.Trim(), ignoreCase: true, out var kind) && Enum.IsDefined(kind), Kind: kind, Item: item))
            .Where(entry => entry.Ok)
            .Select(entry => new SceneInconsistency(entry.Kind, entry.Item.Detail!, entry.Item.Excerpt ?? string.Empty)
            { Severity = entry.Item.Severity ?? string.Empty })
            .ToList();

        return new SceneJudgement(
            scene.Number, scene.Chapter, dto.Accomplishes!, call, Math.Clamp(dto.Tension ?? 0.5, 0, 1),
            dto.Verdict ?? string.Empty, inconsistencies);
    }

    private sealed class SceneJudgementDto
    {
        public string? Accomplishes { get; set; }
        public string? Call { get; set; }
        public double? Tension { get; set; }
        public string? Verdict { get; set; }
        public List<SceneInconsistencyDto>? Inconsistencies { get; set; }
    }

    private sealed class SceneInconsistencyDto
    {
        public string? Severity { get; set; }
        public string? Kind { get; set; }
        public string? Detail { get; set; }
        public string? Excerpt { get; set; }
    }

    public async Task<string> DescribeSceneImage(
        string sceneText, DesignBible bible, string artStyle, IReadOnlyList<string> avoidMoments,
        CancellationToken cancellationToken)
    {
        var userContent = SceneImageContent(sceneText, bible, artStyle, avoidMoments);

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
        string reply;
        try
        {
            reply = await SendMessage(
                ChapterAnalysisInstruction, userContent, cancellationToken, options.ChapterNotesMaxTokens,
                model: options.FastModel);
        }
        catch (ResponseTruncatedException)
        {
            // A dense chunk can outgrow even the full budget, and a truncated reply is unusable. Ask
            // once more with double the room; a second truncation propagates so the chunk fails loudly.
            reply = await SendMessage(
                ChapterAnalysisInstruction, userContent, cancellationToken, options.ChapterNotesMaxTokens * 2,
                model: options.FastModel);
        }
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
                // The omnibus reduce's reviews are never scored; the seven readers replace them.
                .Where(review => ReaderRead.IsWhole(review, scored: false)).ToList(),
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
                .Select(t => new ChapterTension(t.Chapter!, Math.Clamp(t.Tension, 0, 1)) { Driver = t.Driver?.Trim() ?? string.Empty })
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
                e.Suggestion ?? string.Empty, e.Reason ?? string.Empty)
                { Kind = ParseLineEditKind(e.Kind), Severity = e.Severity ?? string.Empty }).ToList(),
            (dto.ProseNotes ?? []).Select(n => new ProseNote(
                n.Chapter ?? string.Empty, n.Pattern ?? string.Empty,
                n.Evidence ?? string.Empty, n.Fix ?? string.Empty)
                { Severity = n.Severity ?? string.Empty }).ToList(),
            (dto.DialogueNotes ?? []).Select(n => new DialogueNote(
                n.Chapter ?? string.Empty, n.Speakers ?? string.Empty,
                n.Observation ?? string.Empty, n.Fix ?? string.Empty)
                { Excerpt = n.Excerpt ?? string.Empty, Severity = n.Severity ?? string.Empty }).ToList(),
            (dto.SceneBreakdown ?? []).Select(s => new SceneBeat(
                s.Chapter ?? string.Empty, s.Scene ?? string.Empty,
                s.Purpose ?? string.Empty, s.Verdict ?? string.Empty)
                { Excerpt = s.Excerpt ?? string.Empty, Outcome = ParseSceneVerdict(s.Outcome) }).ToList());
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
                    d.Chapter ?? string.Empty, d.Excerpt!, d.Divergence ?? string.Empty, d.Suggestion ?? string.Empty)
                { Severity = d.Severity ?? string.Empty })
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
                NormalizePriority(s.Priority))
                { Excerpt = s.Excerpt ?? string.Empty })
            .ToList();
        var showTell = (dto.ShowVsTell ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n.Excerpt))
            .Select(n => new ShowTellNote(
                n.Chapter ?? string.Empty, n.Excerpt!, ParseShowTellDirection(n.Direction), n.Suggestion ?? string.Empty)
                { Severity = n.Severity ?? string.Empty })
            .ToList();

        // The count is measured, not trusted to the model; the model supplies the genre range and the pure
        // LengthGap turns it into the framing line.
        var low = Math.Max(0, dto.TargetLow);
        var high = Math.Max(0, dto.TargetHigh);
        var length = new LengthAssessment(wordCount, low, high, LengthGap.Describe(wordCount, low, high));

        return new ExpansionReport(scenes, showTell, length);
    }

    public async Task<IReadOnlyList<BibleFact>> ReadSeriesBible(
        string manuscript, ReadingFrame frame, string title, string bibleSoFar, CancellationToken cancellationToken)
    {
        var system = ReadingPrompt.System(manuscript, frame, SeriesBibleInstruction(title))
            .Select(block => block.Cached
                ? (object)new { type = "text", text = block.Text, cache_control = new { type = "ephemeral" } }
                : new { type = "text", text = block.Text })
            .ToArray();
        var user = string.IsNullOrWhiteSpace(bibleSoFar)
            ? $"This is the first book of the series read into its bible: \"{title}\"."
            : $"WHAT THE EARLIER BOOKS ESTABLISHED (use the same names for the same things):\n{bibleSoFar}";
        var reply = await SendRaw(system, user, options.PassMaxTokens, options.StandardModel,
            nameof(ReadSeriesBible), options.PassEffort, cancellationToken);

        var json = JsonResponse.ExtractObject(reply)
                   ?? throw new InvalidOperationException("The series bible read came back with no JSON.");
        var dto = JsonResponse.Deserialize<BibleDto>(json)
                  ?? throw new InvalidOperationException("The series bible read came back unreadable.");
        return [.. (dto.Entries ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) && !string.IsNullOrWhiteSpace(entry.Text))
            .Select(entry => new BibleFact(
                Enum.TryParse<BibleKind>(entry.Kind, ignoreCase: true, out var kind) ? kind : BibleKind.Event,
                entry.Name!.Trim(),
                [.. (entry.Aliases ?? []).Where(alias => !string.IsNullOrWhiteSpace(alias)).Select(alias => alias.Trim())],
                entry.Era?.Trim() ?? string.Empty,
                entry.Text!.Trim(),
                entry.Quote?.Trim() ?? string.Empty))];
    }

    private static string SeriesBibleInstruction(string title) =>
        $"You keep the series bible for this book, \"{title}\", a book in a series. List what this book establishes "
        + "that a later or earlier book in the series must stay consistent with: its characters (who they are, how they "
        + "look, what they can do, who they are to each other), its settings, the rules of its world, the events of its "
        + "timeline, and the threads it leaves open. Record only what this book says, as of the point in story time it "
        + "says it; where the book gives a date, an era or a time relative to an earlier book, say so. Do not judge the "
        + "book, and do not suggest changes.\n\n"
        + "Reply with JSON only: {\"entries\": [{\"kind\": \"character, setting, rule, event or thread\", \"name\": "
        + "\"the name the book uses most\", \"aliases\": [\"other names the book uses for it\"], \"era\": \"the story "
        + "time this holds as of, in the book’s own terms, or an empty string\", \"text\": \"what the book establishes, "
        + "in one or two sentences\", \"quote\": \"a short passage (under twenty words) copied verbatim from the book "
        + "that establishes it, or an empty string\"}]}";

    public async Task<StoryPlacement> PlaceInSeries(
        string title, IReadOnlyList<string> otherBooks, string bibleText, string continuitySummary,
        CancellationToken cancellationToken)
    {
        var books = (otherBooks ?? []).Where(book => !string.IsNullOrWhiteSpace(book)).ToList();
        var plainest = StoryPlacement.After(books.Count == 0 ? string.Empty : books[^1]);
        var user =
            $"THIS BOOK: \"{title}\"\n\n"
            + $"THE OTHER BOOKS, in the order they were written: {string.Join(", ", books.Select(book => $"\"{book}\""))}\n\n"
            + $"WHERE THIS BOOK'S STORY STANDS:\n{(string.IsNullOrWhiteSpace(continuitySummary) ? "(not known)" : continuitySummary)}\n\n"
            + bibleText;
        var reply = await SendMessage(PlacementInstruction, user, cancellationToken, options.MaxTokens, model: options.StandardModel);

        var json = JsonResponse.ExtractObject(reply);
        var dto = json is null ? null : JsonResponse.Deserialize<PlacementDto>(json);
        string? Known(string? name) => books.FirstOrDefault(book => string.Equals(book, name?.Trim(), StringComparison.OrdinalIgnoreCase));
        return dto?.Kind?.Trim().ToLowerInvariant() switch
        {
            "after" when Known(dto.Book) is { } book => StoryPlacement.After(book),
            "before" when Known(dto.Book) is { } book => StoryPlacement.Before(book),
            "between" when Known(dto.Book) is { } earlier && Known(dto.OtherBook) is { } later => StoryPlacement.Between(earlier, later),
            "era" when !string.IsNullOrWhiteSpace(dto.Era) => StoryPlacement.InEra(dto.Era),
            _ => plainest,
        };
    }

    private const string PlacementInstruction =
        "You place a book in its series' story time. Given the series bible of the books written before it and where "
        + "this book’s story stands, say where it takes place against them: after one book, before one book, between "
        + "two books, or, when it is set far from all of them, in an era of its own, named in the series' own terms. "
        + "Use only the book titles given.\n\n"
        + "Reply with JSON only: {\"kind\": \"after, before, between or era\", \"book\": \"a title (the earlier one, "
        + "for between)\", \"otherBook\": \"the later title, for between, else an empty string\", \"era\": \"the era, "
        + "for era, else an empty string\"}";

    // The round table (release H). Every call, host's and readers' alike, opens with the same two cached
    // blocks, the panel's independent reads and the rules of the show; then the transcript, one block per
    // line with the cache mark on the last, so each call reads the one before it from the cache and pays only
    // for the newest line; then, uncached, who is speaking and what they have been asked.
    public async Task<HostMove> RoundTableHost(
        IReadOnlyList<Review> reviews, RoundTable table, IReadOnlyList<RoundTableCue> owed, int roundsLeft,
        StoryLength length, CancellationToken cancellationToken)
    {
        var panelIds = string.Join("; ", reviews.Select(review => $"{review.PersonaId} = {RoundTablePlan.Name(review.PersonaId)}"));
        var splits = RoundTablePlan.Splits(reviews).Count > 0;
        var uncovered = RoundTablePlan.Uncovered(table).Count;
        var agenda = table.Agenda.Count == 0
            ? string.Empty
            : "\nAGENDA FOR THE OPEN DISCUSSION:\n" + string.Join("\n", table.Agenda.Select(item =>
                $"- {item.Topic} ({(table.Covered.Contains(item.Topic, StringComparer.OrdinalIgnoreCase) ? "raised" : "still to raise")})"
                + (item.Why.Length > 0 ? $": {item.Why}" : string.Empty)));
        var opening = table.Turns.Count > 0
            ? string.Empty
            : $"\n\nTHE SHOW IS STARTING. Open it: welcome the listeners, say you are talking about a "
              + $"{ManuscriptLength.Noun(length)}, and bring the panel in by name in a line or two. "
              + (splits
                  ? "Then name where the panel splits and hand to one side of it."
                  : "No two readers' scores are three or more apart, so say the panel landed close together and go "
                    + "straight into the first agenda topic.");
        var part = owed.Count > 0
            ? "\n\nPART OF THE SHOW: THE DISAGREEMENTS. Work through where the readers split before anything else."
            : "\n\nPART OF THE SHOW: OPEN DISCUSSION. "
              + (splits ? "The disagreements have been aired; " : "There are no wide score splits on this panel; ")
              + "take the panel through the agenda, one topic at a time, and name the topic you are on.";
        var pace = roundsLeft > 0
            ? $"\n\nROUNDS LEFT: about {roundsLeft}, with {uncovered} agenda topic{(uncovered == 1 ? "" : "s")} still to raise. "
              + "Pace the topics to fit: stay on a topic for another round while the panel still has something to say about it, "
              + "and move on when it is talked out."
            : uncovered > 0
                ? $"\n\nROUNDS LEFT: none, but {uncovered} agenda topic{(uncovered == 1 ? "" : "s")} must still be raised. "
                  + "Move briskly to the next one; do not say time is up."
                : "\n\nROUNDS LEFT: none. Every topic has been raised; you may close.";
        var debts = owed.Count == 0
            ? "\nEvery rebuttal owed has been given."
            : "\nREBUTTALS STILL OWED (each must happen before you close): "
              + string.Join("; ", owed.Select(cue => $"{RoundTablePlan.Name(cue.Speaker)} should answer {RoundTablePlan.Name(cue.Answering)}"));
        var role =
            $"You are {RoundTablePlan.HostName}, the host. Your voice: {RoundTablePlan.HostVoice}. Speak one to three "
            + "sentences, then hand the floor to the reader who should start this round and ask them something; the others "
            + "will come in after them. React like a person to what was just said; do not recap the last speaker before "
            + "every question. Never thank the panel, say goodbye or say time is up: the show's closing words are a "
            + "separate step. Close only when every agenda topic has been raised and the conversation has run its course; "
            + "when you close, leave \"says\" empty."
            + opening + part + agenda + pace + debts
            + $"\nPANEL IDS: {panelIds}.\n\n"
            + "Reply with JSON only: {\"says\": \"what you say aloud now\", \"next\": \"the panel id of who starts this "
            + "round, or an empty string when closing\", \"ask\": \"what you ask them\", \"answering\": \"the panel id of "
            + "whom they should answer, or an empty string\", \"topic\": \"in the open discussion, the agenda topic you are "
            + "on, exactly as the agenda names it; else an empty string\", \"close\": false}";
        var reply = await SendRoundTable(reviews, table, role, nameof(RoundTableHost), cancellationToken);

        var json = JsonResponse.ExtractObject(reply)
                   ?? throw new InvalidOperationException("The round table host’s move came back with no JSON.");
        var dto = JsonResponse.Deserialize<HostMoveDto>(json)
                  ?? throw new InvalidOperationException("The round table host’s move came back unreadable.");
        return new HostMove(dto.Says?.Trim() ?? string.Empty, dto.Next?.Trim() ?? string.Empty,
            dto.Ask?.Trim() ?? string.Empty, dto.Answering?.Trim() ?? string.Empty, dto.Close)
        {
            Topic = dto.Topic?.Trim() ?? string.Empty,
        };
    }

    /// <summary>The word a reader offered a chance to come in replies with when they would only echo.</summary>
    public const string RoundTablePass = "PASS";

    /// <summary>
    /// One reader's line, or, when the cue let them pass and they did, an empty string.
    /// </summary>
    public async Task<string> RoundTableSpeak(
        Persona persona, IReadOnlyList<Review> reviews, RoundTable table, RoundTableCue cue, CancellationToken cancellationToken)
    {
        var name = persona.ReaderName ?? persona.Name;
        var role =
            $"You are {name}, the {persona.Name.ToLowerInvariant()}. Your voice: {persona.Voice}. The voice shapes how you "
            + "talk, never what you judge. Your own independent read is in the panel above under your name: it is your "
            + "memory of the book.\n\n"
            + (cue.MayPass
                ? $"{RoundTablePlan.HostName}'s question is still on the table, and it is your chance to come in. {cue.Ask} "
                  + $"If you would only be agreeing with or repeating what has already been said, reply with exactly {RoundTablePass}."
                : $"{RoundTablePlan.HostName} has put this to you: {cue.Ask}")
            + (cue.Answering.Length > 0 ? $"\nAnswer {RoundTablePlan.Name(cue.Answering)} directly." : string.Empty);
        var reply = (await SendRoundTable(reviews, table, role, $"{nameof(RoundTableSpeak)}[{persona.Id}]", cancellationToken)).Trim();

        if (reply.TrimEnd('.', '!', ' ').Equals(RoundTablePass, StringComparison.OrdinalIgnoreCase))
        {
            return cue.MayPass
                ? string.Empty
                : throw new InvalidOperationException($"{name} passed when {RoundTablePlan.HostName} asked them directly.");
        }
        // A speaker that labels its own line has the label taken off: the page prints the name.
        foreach (var label in new[] { name, persona.Name })
            if (reply.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase))
                reply = reply[(label.Length + 1)..].Trim();
        return reply.Length > 0 ? reply : throw new InvalidOperationException($"{name} said nothing at the round table.");
    }

    public async Task<RoundTableClosing> RoundTableClose(
        IReadOnlyList<Review> reviews, RoundTable table, CancellationToken cancellationToken)
    {
        var role =
            $"You are {RoundTablePlan.HostName}, the host. Your voice: {RoundTablePlan.HostVoice}. The conversation is over: "
            + "close the show in two or three sentences, thanking the panel and the listeners. These are the show's only "
            + "goodbye. Then say where the readers ended up: what they came to agree on, and where they still differ. You "
            + "never say who is right, and never rule on a split; lay it out. Reply with JSON only: {\"says\": \"your "
            + "closing words\", \"agreed\": [\"one point they agree on, in a sentence\"], \"stillDiffer\": [\"one point "
            + "they still differ on, naming who stands where\"]}";
        var reply = await SendRoundTable(reviews, table, role, nameof(RoundTableClose), cancellationToken);
        var json = JsonResponse.ExtractObject(reply)
                   ?? throw new InvalidOperationException("The round table’s close came back with no JSON.");
        var dto = JsonResponse.Deserialize<ClosingDto>(json)
                  ?? throw new InvalidOperationException("The round table’s close came back unreadable.");
        return new RoundTableClosing(dto.Says?.Trim() ?? string.Empty, new RoundTableSummary(
            [.. (dto.Agreed ?? []).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim())],
            [.. (dto.StillDiffer ?? []).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim())]));
    }

    // The rules of the show, the same for every speaker so every call shares the cached prefix.
    private const string RoundTableRules =
        "THE SHOW\n\nThis is a podcast round table. The host, " + RoundTablePlan.HostName + ", and seven readers talk about "
        + "a manuscript each reader has read on their own; their independent reads and scores are in the panel above. It "
        + "runs in rounds: the host says something and asks one reader, that reader answers, and then each of the other "
        + "readers gets a chance to come in, replying to whoever they like by name, before the host speaks again. First "
        + "come the disagreements, where readers scored three or more points apart; then an open discussion through an "
        + "agenda of topics. It should sound like people talking: loose, warm, quick, with reactions, agreement, "
        + "pushback and the odd joke, not a series of statements. A reader's line is one to four sentences of plain "
        + "prose. Readers may concede a point in words, but every score stays what it was in its read. Talk about the "
        + "book as it was read; do not invent passages. No speaker labels and no stage directions. The host never says "
        + "who is right.";

    // The panel's independent reads: the same text for every call of a show, so it is one cached prefix.
    private static string Panel(IReadOnlyList<Review> reviews) =>
        "THE PANEL'S INDEPENDENT READS\n\n" + string.Join("\n\n", reviews.Select(review =>
        {
            var persona = PersonaCatalogue.Readers.FirstOrDefault(reader => reader.Id == review.PersonaId);
            var question = persona?.Question ?? string.Empty;
            return $"{RoundTablePlan.Name(review.PersonaId)} ({persona?.Name ?? review.PersonaId}), asked \"{question}\": "
                + (review.Score > 0 ? $"{review.Score}/10 — {review.ScoreReason}" : "unscored")
                + $"\nVerdict: {review.Verdict}\nAssessment: {review.Assessment}"
                + $"\nStrengths: {string.Join("; ", review.Strengths)}\nWeaknesses: {string.Join("; ", review.Weaknesses)}"
                + $"\nWould need: {review.Condition}";
        }));

    // The transcript, one block per line: a block once sent never changes, so the next call's prefix holds it.
    private static List<object> Transcript(RoundTable table)
    {
        if (table.Turns.Count == 0)
            return [new { type = "text", text = "THE CONVERSATION SO FAR: nothing yet; the show is about to begin." }];
        var blocks = table.Turns
            .Select((turn, i) => (i == 0 ? "THE CONVERSATION SO FAR:\n\n" : string.Empty)
                + $"{RoundTablePlan.Name(turn.Speaker)}: {turn.Words}\n\n")
            .ToList();
        return
        [
            .. blocks.Take(blocks.Count - 1).Select(text => (object)new { type = "text", text }),
            new { type = "text", text = blocks[^1], cache_control = new { type = "ephemeral" } },
        ];
    }

    private Task<string> SendRoundTable(
        IReadOnlyList<Review> reviews, RoundTable table, string role, string call, CancellationToken cancellationToken)
    {
        object[] system =
        [
            new { type = "text", text = Panel(reviews), cache_control = new { type = "ephemeral" } },
            new { type = "text", text = RoundTableRules, cache_control = new { type = "ephemeral" } },
        ];
        List<object> user = [.. Transcript(table), new { type = "text", text = role }];
        return SendRaw(system, user, options.RoundTableMaxTokens, options.StrongModel, call, options.Effort, cancellationToken);
    }

    public async Task<SeriesContinuity> AssessSeriesContinuity(
        string title, SeriesCanon priorCanon, string bibleText, StoryPlacement? placement,
        IReadOnlyList<ChapterNotes> notes, string continuitySummary, CancellationToken cancellationToken)
    {
        var canonJson = JsonSerializer.Serialize(priorCanon, JsonResponse.CaseInsensitive);
        var notesJson = JsonSerializer.Serialize(notes, JsonResponse.CaseInsensitive);
        // The bible, when the series has one, is what the book is checked against (R24); the prior book's
        // canon is still sent for its open threads, which the carried-thread verdicts answer one by one.
        var against = string.IsNullOrWhiteSpace(bibleText)
            ? $"PRIOR BOOK'S CANON (JSON):\n{canonJson}\n\n"
            : $"{bibleText}\n\nTHE PRIOR BOOK'S OPEN THREADS (JSON):\n"
              + JsonSerializer.Serialize(priorCanon.OpenThreads, JsonResponse.CaseInsensitive) + "\n\n";
        var where = placement is null
            ? string.Empty
            : $"WHERE THIS BOOK SITS IN THE SERIES' STORY TIME: {placement.Where}"
              + (placement.Confirmed ? " (the author’s word)" : " (Folio’s guess, not yet confirmed by the author)") + "\n\n";
        var userContent =
            $"THIS BOOK'S TITLE: {title}\n\n" +
            where +
            against +
            $"THIS BOOK'S STORY STATE:\n{(string.IsNullOrWhiteSpace(continuitySummary) ? "(none)" : continuitySummary)}\n\n" +
            $"THIS BOOK'S PER-CHUNK NOTES (JSON array, in reading order):\n{notesJson}";

        // Best-effort: a malformed or empty reply yields no breaks/threads (keeping the prior book's title), so
        // the continuity read never blocks the report.
        var assumption = placement?.Assumption ?? string.Empty;
        var empty = SeriesContinuity.Empty with { PriorBookTitle = priorCanon.PriorBookTitle, Assumption = assumption };

        var reply = await SendMessage(
            SeriesContinuityInstruction + (string.IsNullOrWhiteSpace(bibleText) ? string.Empty : SeriesBibleCheckInstruction),
            userContent, cancellationToken, options.AnalysisMaxTokens);
        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
            return empty;

        var dto = JsonResponse.Deserialize<SeriesContinuityDto>(json);
        if (dto is null)
            return empty;

        var breaks = (dto.Breaks ?? [])
            .Where(b => !string.IsNullOrWhiteSpace(b.Contradiction))
            .Select(b => new SeriesBreak(b.Chapter ?? string.Empty, b.Established ?? string.Empty, b.Contradiction!)
            {
                Excerpt = b.Excerpt ?? string.Empty,
                Severity = b.Severity ?? string.Empty,
                // Only a read against the bible sorts its findings into the four kinds and quotes the earlier book.
                Kind = string.IsNullOrWhiteSpace(bibleText) ? string.Empty : ContinuityKinds.Normalize(b.Kind),
                Book = b.Book?.Trim() ?? string.Empty,
                Quote = b.Quote?.Trim() ?? string.Empty,
            })
            .ToList();
        var carried = (dto.CarriedThreads ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t.Established))
            .Select(t => new CarriedThread(t.Established!, NormalizeThreadStatus(t.Status)))
            .ToList();

        return new SeriesContinuity(priorCanon.PriorBookTitle, breaks, carried) { Assumption = assumption };
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

    private static LineEditKind ParseLineEditKind(string? kind) =>
        Enum.TryParse<LineEditKind>(kind, ignoreCase: true, out var parsed) ? parsed : LineEditKind.Other;

    // Defaults to EarnsItsPlace rather than DoesNoWork: an unparseable outcome should not strike
    // through a scene the model never actually judged as dead weight.
    private static SceneVerdict ParseSceneVerdict(string? outcome) =>
        Enum.TryParse<SceneVerdict>(outcome, ignoreCase: true, out var parsed) ? parsed : SceneVerdict.EarnsItsPlace;

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
    // The summary is the section's whole entry in the whole-book notes (R16): one short paragraph, two
    // sentences for a short story so its notes fit one page. The findings go on the manuscript.
    private static string SectionFormat(string manuscript) =>
        "Reply with JSON only: {\"summary\": \"one short paragraph on what you found overall, at most "
        + (ManuscriptLength.Classify(manuscript) == StoryLength.ShortStory ? "two" : "four")
        + " sentences, or an empty string if this book gives you nothing to say\", \"findings\": [{\"chapter\": \"Ch. 12-15\", "
        + "\"detail\": \"what is wrong and why it matters\", \"excerpt\": \"a short phrase (under fifteen "
        + "words) copied verbatim from the manuscript where it happens, or an empty string when it is about "
        + "the whole book\", \"severity\": \"Must if the book does not work until it is fixed, Should if it "
        + "weakens the book, Consider if it is a refinement\"}]}. Finding nothing is a legitimate answer: "
        + "return an empty findings list rather than inventing one.";

    private const string ReaderFormat =
        "Reply with JSON only: {\"verdict\": \"your answer in one clause\", \"assessment\": \"your full "
        + "review in character, several hundred words\", \"strengths\": [\"what works, in your terms\"], "
        + "\"weaknesses\": [\"what does not, in your terms\"], \"condition\": \"what would have to change "
        + "before your verdict changed\", \"score\": a whole number from 1 to 10 against your anchors, "
        + "\"scoreReason\": \"one sentence on why the score is that number\"}. Every field is required. If you cannot fill one of them you "
        + "have not finished reading: a short answer is worse than none, because the page prints it.";

    // Built rather than a const: the noun has to name what this manuscript actually is (a 1,200-word
    // short story is not "one novel"), and that depends on the measured word count these passes never
    // see the manuscript to derive for themselves.
    // The counts in the synthesis instructions come from the catalogue, so a reader added or dropped
    // cannot leave the prompt describing a panel that no longer exists.
    private static string Spelled(int count) => Folio.Domain.Shelf.BookLines.Spell(count);

    private static string RevisionPlanInstruction(StoryLength length) =>
        $"You are given {Spelled(PersonaCatalogue.All.Count)} independent reads of one {ManuscriptLength.Noun(length)}: "
        + $"{Spelled(PersonaCatalogue.Sections.Count)} structural sections and {Spelled(PersonaCatalogue.Readers.Count)} readers, none of whom saw the others' notes. Produce the author’s revision plan. "
        + "Where several reads name the same thing, that is one item and it ranks higher for it. Reply with "
        + "JSON only: {\"revisionPlan\": [{\"priority\": \"Must|Should|Consider\", \"chapter\": \"Ch. 12-15\", "
        + "\"issue\": \"what is wrong\", \"suggestedFix\": \"what to do about it — a direction, never "
        + "replacement prose\", \"draws\": [\"f1a2b3c4d\"]}]}. You diagnose and point; you never write the author’s sentences for them. "
        + "You are also given \"findings\": every note written on the manuscript, each with an \"id\". In "
        + "\"draws\", copy the ids of the findings each item rests on, most telling first — the author follows "
        + "the first one to the page — and leave it empty when no listed finding is behind the item.";

    // Said only on a later draft, after the plan's own instruction (R19).
    private const string ComparisonInstruction =
        " You are also given \"previousPlan\": the last draft’s plan, numbered. Add to each item \"since\": "
        + "\"new\" if the last plan had nothing like it, \"open\" if the last plan raised it and it still stands, or "
        + "\"regressed\" if the last draft had fixed it or made it smaller and it has come back or grown. And add, "
        + "beside revisionPlan, \"fixed\": the numbers of the last plan’s items that no read raises any more.";

    public async Task<SectionRead> ReadSection(
        Persona persona, string manuscript, ReadingFrame frame, CancellationToken cancellationToken)
    {
        var reply = await SendPass(persona, manuscript, frame, SectionFormat(manuscript), cancellationToken);
        return ReadSectionReply(persona, reply);
    }

    // A section's reply, parsed; the one place it is, for the direct pass and the batch.
    private SectionRead ReadSectionReply(Persona persona, string reply)
    {
        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
        {
            // A section is allowed to find nothing, so an unparseable reply degrades to silence
            // rather than failing the read. The pass is still recorded as done: it ran, and a section
            // with nothing to say prints nothing. It was still billed, though, and a reply that is not JSON
            // at all is not the same as a section with nothing to say, so the log says which this was.
            logger?.LogWarning(
                "Claude call {Call}[{Persona}] replied with nothing readable as JSON ({Characters} characters); " +
                "recorded as a silent section. The call was billed", nameof(ReadSection), persona.Id, reply.Length);
            return SectionRead.Silent(persona.Id);
        }

        var dto = JsonResponse.Deserialize<SectionDto>(json);
        return new SectionRead(
            persona.Id,
            dto?.Summary ?? string.Empty,
            (dto?.Findings ?? []).Where(finding => !string.IsNullOrWhiteSpace(finding.Detail))
                .Select(finding => new ReportFinding(finding.Chapter ?? string.Empty, finding.Detail!)
                { Excerpt = finding.Excerpt ?? string.Empty, Severity = finding.Severity ?? string.Empty })
                .ToList());
    }

    public async Task<Review> ReadAsReader(
        Persona persona, string manuscript, ReadingFrame frame, CancellationToken cancellationToken)
    {
        var reply = await SendPass(persona, manuscript, frame, ReaderFormat, cancellationToken);
        return ReadReaderReply(persona, reply);
    }

    // A reader's reply, parsed and held to the whole-read rule; the one place it is, for both paths.
    private static Review ReadReaderReply(Persona persona, string reply)
    {
        var json = JsonResponse.ExtractObject(reply)
            ?? throw new InvalidOperationException(
                $"{persona.Name} replied with nothing this could read as a review. A reader’s pass is "
                + "all-or-nothing: a page that prints half a read is worse than one that admits the "
                + "read failed.");

        var dto = JsonResponse.Deserialize<ReviewDto>(json);
        var review = new Review(
            persona.Id,
            dto?.Verdict ?? string.Empty,
            dto?.Assessment ?? string.Empty,
            dto?.Strengths ?? [],
            dto?.Weaknesses ?? [],
            dto?.Condition ?? dto?.Recommendation ?? string.Empty)
        {
            Score = ScoreOf(dto?.Score),
            ScoreReason = dto?.ScoreReason ?? string.Empty,
        };

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
        IReadOnlyList<FindingEntry> findings,
        IReadOnlyList<RevisionItem> previousPlan,
        CancellationToken cancellationToken)
    {
        // The synthesis passes read the other fourteen rather than the manuscript, so they carry no
        // cached prefix and cost nothing to run against either model. The plan is also shown the keyed
        // findings (R15), as id, kind, chapter and words, so its items can name the ones they rest on.
        var listed = (findings ?? []).Select(entry => new
        {
            id = entry.Key, kind = entry.Label, chapter = entry.Chapter, severity = entry.Severity.ToString(), note = entry.Text,
        });
        // On a later draft the plan is also shown the last plan, numbered, to say where each item stands
        // against it and which of its items are now fixed (R19).
        IReadOnlyList<RevisionItem> previous = previousPlan ?? [];
        var evidence = previous.Count == 0
            ? JsonSerializer.Serialize(new { sections, readers, findings = listed }, JsonResponse.CaseInsensitive)
            : JsonSerializer.Serialize(new
            {
                sections, readers, findings = listed,
                previousPlan = previous.Select((item, i) => new { number = i + 1, item.Priority, item.Chapter, item.Issue }),
            }, JsonResponse.CaseInsensitive);
        var known = (findings ?? []).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        var length = ManuscriptLength.Classify(wordCount);
        var instruction = RevisionPlanInstruction(length) + (previous.Count == 0 ? string.Empty : ComparisonInstruction);
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
                item.Issue ?? string.Empty, item.SuggestedFix ?? string.Empty)
                {
                    // Only ids it was shown: a made-up one would be a link to nowhere.
                    Draws = [.. (item.Draws ?? []).Where(key => known.Contains(key)).Distinct()],
                    Since = item.Since?.Trim() ?? string.Empty,
                }).ToList(),
            dto?.Verdict is { } verdict
                ? new Verdict(verdict.Strengths ?? [], verdict.Problems ?? [], verdict.MostNeeds ?? string.Empty)
                {
                    Standing = verdict.Standing ?? string.Empty,
                    Reading = verdict.Reading ?? string.Empty,
                }
                : Verdict.Empty)
        {
            // Only numbers it was shown: a made-up one names nothing.
            Fixed = [.. (dto?.Fixed ?? []).Distinct().Where(n => n >= 1 && n <= previous.Count).Select(n => previous[n - 1])],
        };
    }

    /// <summary>
    /// Sends one persona's pass, with the MANUSCRIPT FIRST and everything persona-specific after it.
    ///
    /// <para>It does not go through <see cref="SendMessage"/>, and that is the point. SendMessage puts
    /// its instruction in the first cached block, which is right when the instruction is constant for
    /// a call type and catastrophic here: the instruction names the persona, so fourteen passes would
    /// be fourteen different cache prefixes and the manuscript would be written fourteen times
    /// instead of twice (once per model) — measured at roughly $17 a draft against $2 (F50), with nothing
    /// visible wrong. <c>ReadingPrompt</c> decides the order and a guard holds it.</para>
    /// </summary>
    private Task<string> SendPass(
        Persona persona, string manuscript, ReadingFrame frame, string format, CancellationToken cancellationToken,
        [CallerMemberName] string call = "")
    {
        var pass = PassRequest(persona, manuscript, frame, format);
        return SendRaw(pass.System, pass.User, pass.MaxTokens, pass.Model, $"{call}[{persona.Id}]", options.PassEffort, cancellationToken);
    }

    // One whole-book pass's request: the one place it is built, for the direct pass and the batch, so a
    // batched pass is the same request (same prefix, same cache) as the direct one.
    private (object[] System, string User, int MaxTokens, string Model) PassRequest(
        Persona persona, string manuscript, ReadingFrame frame, string format)
    {
        var system = ReadingPrompt.System(manuscript, persona, frame)
            .Select(block => block.Cached
                ? (object)new { type = "text", text = block.Text, cache_control = new { type = "ephemeral" } }
                : new { type = "text", text = block.Text })
            .ToArray();
        var length = ManuscriptLength.Classify(manuscript);
        return (
            system,
            ReadingPrompt.Question(persona, length) + "\n\n" + format,
            persona.Kind == PersonaKind.Reader && length == StoryLength.Novel ? options.NovelReaderMaxTokens : options.PassMaxTokens,
            persona.Tier == ModelTier.Strong ? options.StrongModel : options.StandardModel);
    }

    private Task<string> SendMessage(
        string instruction, string userText, CancellationToken cancellationToken,
        int? maxTokens = null, string? cachedContext = null, string? model = null,
        [CallerMemberName] string call = "")
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
            string.IsNullOrWhiteSpace(model) ? options.Model : model, call, options.Effort, cancellationToken);
    }

    // The transport both paths share: everything below this line is the same whatever the system
    // blocks say, so the ordering decision lives entirely in the two callers above.
    private async Task<string> SendRaw(
        object[] system, object userText, int effectiveMaxTokens, string model, string call, string effort,
        CancellationToken cancellationToken)
    {
        // Every call is accounted for here, success or failure, because this is the one place that knows
        // what was sent and what came back. A reply that dies after minutes of streaming has still been
        // billed for its input; without a line per call there is no way to tell a book that is working
        // from one that is paying for the same read again and again.
        var usage = new CallUsage();
        var started = Stopwatch.GetTimestamp();
        try
        {
            var text = await SendRawCore(system, userText, effectiveMaxTokens, model, effort, usage, cancellationToken);
            logger?.LogInformation(
                "Claude call {Call} succeeded in {Seconds:F1}s: model={Model} input={Input} cacheWrite={CacheWrite} " +
                "cacheRead={CacheRead} output={Output} stop={Stop} requestId={RequestId}",
                call, Elapsed(started), model, usage.Input, usage.CacheWrite, usage.CacheRead, usage.Output,
                usage.StopReason, usage.RequestId);
            return text;
        }
        catch (OperationCanceledException)
        {
            // Android stopping the worker mid-stream throws nothing a person would ever see: the process
            // simply ends. This line is the only trace that the input was billed and the reply discarded.
            logger?.LogWarning(
                "Claude call {Call} cancelled after {Seconds:F1}s ({Characters} characters streamed): " +
                "model={Model} input={Input} cacheWrite={CacheWrite} cacheRead={CacheRead} requestId={RequestId}. " +
                "The worker was stopped mid-call; the input was billed and the reply is discarded",
                call, Elapsed(started), usage.Characters, model, usage.Input, usage.CacheWrite, usage.CacheRead,
                usage.RequestId);
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception,
                "Claude call {Call} failed after {Seconds:F1}s: model={Model} input={Input} cacheWrite={CacheWrite} " +
                "cacheRead={CacheRead} output={Output} stop={Stop} requestId={RequestId}, " +
                "streamed {Characters} characters: {Error}",
                call, Elapsed(started), model, usage.Input, usage.CacheWrite, usage.CacheRead, usage.Output,
                usage.StopReason, usage.RequestId, usage.Characters, ErrorText.Describe(exception));
            throw;
        }
    }

    private static double Elapsed(long started) => Stopwatch.GetElapsedTime(started).TotalSeconds;

    private async Task<string> SendRawCore(
        object[] system, object userText, int effectiveMaxTokens, string model, string effort, CallUsage usage,
        CancellationToken cancellationToken)
    {
        // Effort is set explicitly, never inherited. See ClaudeOptions.Effort.
        var payload = new
        {
            model,
            max_tokens = effectiveMaxTokens,
            stream = true,
            output_config = new { effort },
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
        usage.RequestId = response.Headers.TryGetValues("request-id", out var ids) ? ids.FirstOrDefault() : null;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            // Only a credential rejection flags the key; throttling/5xx/network are transient and left alone.
            if (AuthFailure.IsHttpCredentialRejection((int)response.StatusCode, body))
                healthSink?.MarkFailing(CredentialIds.Claude, $"{(int)response.StatusCode} {response.StatusCode}");
            throw ToException(response, body);
        }

        var text = await ReadStreamedText(response, effectiveMaxTokens, usage, cancellationToken);
        // A successful authenticated call clears any prior failure on this key.
        healthSink?.MarkOk(CredentialIds.Claude);
        return text;
    }

    // What one call sent and got back, filled in as the stream arrives so a call that dies part-way can
    // still report what it had cost by then.
    private sealed class CallUsage
    {
        public int Input { get; set; }
        public int CacheWrite { get; set; }
        public int CacheRead { get; set; }
        public int Output { get; set; }
        public string? StopReason { get; set; }
        public string? RequestId { get; set; }
        public int Characters { get; set; }
    }

    // Accumulates the assistant's text from an Anthropic Messages streaming response. Only text_delta
    // events contribute; a mid-stream error event is surfaced with the same classification as an HTTP
    // error so the worker reschedules throttling rather than crashing the cycle. The message_delta
    // event carries the final stop_reason: if the model ran out of output budget ("max_tokens") the
    // accumulated text is truncated, so we raise instead of returning a partial (unparseable) body.
    private static async Task<string> ReadStreamedText(
        HttpResponseMessage response, int maxTokens, CallUsage usage, CancellationToken cancellationToken)
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
                case "message_start"
                    when root.TryGetProperty("message", out var message) &&
                         message.TryGetProperty("usage", out var startUsage):
                    ReadUsage(startUsage, usage);
                    break;

                case "content_block_delta"
                    when root.TryGetProperty("delta", out var delta) &&
                         delta.TryGetProperty("type", out var deltaType) &&
                         deltaType.GetString() == "text_delta" &&
                         delta.TryGetProperty("text", out var chunk):
                    text.Append(chunk.GetString());
                    usage.Characters = text.Length;
                    break;

                case "message_delta"
                    when root.TryGetProperty("delta", out var messageDelta) &&
                         messageDelta.TryGetProperty("stop_reason", out var reason) &&
                         reason.ValueKind == JsonValueKind.String:
                    stopReason = reason.GetString();
                    usage.StopReason = stopReason;
                    if (root.TryGetProperty("usage", out var endUsage))
                        ReadUsage(endUsage, usage);
                    break;

                case "error":
                    throw StreamError(root);

                case "message_stop":
                    return Finish(text, stopReason, maxTokens);
            }
        }

        return Finish(text, stopReason, maxTokens);
    }

    // message_start carries the input side (and a placeholder output count); message_delta carries the final
    // output count. A field absent from an event leaves the earlier figure alone.
    private static void ReadUsage(JsonElement element, CallUsage usage)
    {
        if (element.TryGetProperty("input_tokens", out var input) && input.TryGetInt32(out var inputTokens))
            usage.Input = inputTokens;
        if (element.TryGetProperty("cache_creation_input_tokens", out var write) && write.TryGetInt32(out var writeTokens))
            usage.CacheWrite = writeTokens;
        if (element.TryGetProperty("cache_read_input_tokens", out var read) && read.TryGetInt32(out var readTokens))
            usage.CacheRead = readTokens;
        if (element.TryGetProperty("output_tokens", out var output) && output.TryGetInt32(out var outputTokens))
            usage.Output = outputTokens;
    }

    private static string Finish(StringBuilder text, string? stopReason, int maxTokens)
    {
        if (stopReason == "max_tokens")
            throw new ResponseTruncatedException(
                $"Claude response was truncated at the {maxTokens}-token output limit " +
                "(stop_reason=max_tokens); the response is incomplete. Increase the request’s " +
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

        // An empty credit balance arrives as an ordinary 400. Classed as a plain failure it spent the
        // attempt budget of every unit in a book within seconds and gave up on all of them for good, when
        // all that was wrong was a top-up away; flagged as exhausted quota it is waited out instead.
        if (body.Contains("credit balance is too low", StringComparison.OrdinalIgnoreCase))
        {
            return new RateLimitException(
                $"Anthropic credit balance is too low: {(int)status} ({status}): {body}",
                kind: RateLimitKind.QuotaExhausted);
        }

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

    private sealed class HostMoveDto
    {
        public string? Says { get; set; }
        public string? Next { get; set; }
        public string? Ask { get; set; }
        public string? Answering { get; set; }
        public string? Topic { get; set; }
        public bool Close { get; set; }
    }

    private sealed class ClosingDto
    {
        public string? Says { get; set; }
        public List<string>? Agreed { get; set; }
        public List<string>? StillDiffer { get; set; }
    }

    private sealed class PlacementDto
    {
        public string? Kind { get; set; }
        public string? Book { get; set; }
        public string? OtherBook { get; set; }
        public string? Era { get; set; }
    }

    private sealed class BibleDto
    {
        public List<BibleEntryDto>? Entries { get; set; }
    }

    private sealed class BibleEntryDto
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
        public List<string>? Aliases { get; set; }
        public string? Era { get; set; }
        public string? Text { get; set; }
        public string? Quote { get; set; }
    }

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
        public string? Excerpt { get; set; }
    }

    private sealed class ShowTellDto
    {
        public string? Severity { get; set; }
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
        public string? Kind { get; set; }
        public string? Book { get; set; }
        public string? Quote { get; set; }
        public string? Severity { get; set; }
        public string? Chapter { get; set; }
        public string? Established { get; set; }
        public string? Contradiction { get; set; }
        public string? Excerpt { get; set; }
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
        public string? Driver { get; set; }
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
        public string? Severity { get; set; }
        public string? Chapter { get; set; }
        public string? Excerpt { get; set; }
        public string? Suggestion { get; set; }
        public string? Reason { get; set; }
        public string? Kind { get; set; }
    }

    private sealed class ProseNoteDto
    {
        public string? Severity { get; set; }
        public string? Chapter { get; set; }
        public string? Pattern { get; set; }
        public string? Evidence { get; set; }
        public string? Fix { get; set; }
    }

    private sealed class DialogueNoteDto
    {
        public string? Severity { get; set; }
        public string? Chapter { get; set; }
        public string? Speakers { get; set; }
        public string? Observation { get; set; }
        public string? Fix { get; set; }
        public string? Excerpt { get; set; }
    }

    private sealed class SceneBeatDto
    {
        public string? Chapter { get; set; }
        public string? Scene { get; set; }
        public string? Purpose { get; set; }
        public string? Verdict { get; set; }
        public string? Excerpt { get; set; }
        public string? Outcome { get; set; }
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
        public string? Severity { get; set; }
        public string? Chapter { get; set; }
        public string? Detail { get; set; }
        public string? Excerpt { get; set; }
    }

    private sealed class SynthesisDto
    {
        public List<int>? Fixed { get; set; }
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
        public JsonElement? Score { get; set; }
        public string? ScoreReason { get; set; }
    }

    // A score as the model wrote it: a number, a number in quotes, or a half point, rounded to the
    // whole number the page prints. Anything else is zero, which ReaderRead calls a short read.
    private static int ScoreOf(JsonElement? score)
    {
        if (score is not { } element)
            return 0;

        double value;
        if (element.ValueKind == JsonValueKind.Number)
            value = element.GetDouble();
        else if (element.ValueKind != JsonValueKind.String
                 || !double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return 0;

        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private sealed class RevisionItemDto
    {
        public string? Since { get; set; }
        public List<string>? Draws { get; set; }
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
        public string? Severity { get; set; }
        public string? Chapter { get; set; }
        public string? Excerpt { get; set; }
        public string? Divergence { get; set; }
        public string? Suggestion { get; set; }
    }
}
