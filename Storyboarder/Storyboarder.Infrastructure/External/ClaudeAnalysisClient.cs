using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Model;
using Storyboarder.Domain.Pipeline;
using Storyboarder.Infrastructure.Configuration;

namespace Storyboarder.Infrastructure.External;

// Literary analysis via the Anthropic Messages API. Modeled on the repo's existing Claude client:
// x-api-key + anthropic-version headers, a prompt-cached system instruction, and descriptive
// failures. Provider throttling is surfaced as RateLimitException so the worker can reschedule.
public sealed class ClaudeAnalysisClient : IClaudeAnalysisClient
{
    private const string MessagesUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    // Anthropic's hosted web-search tool, used only by the fact-check call. The versioned type string
    // is the API's identifier for it; if the tool is ever renamed this is the one place to change.
    private const string WebSearchToolType = "web_search_20250305";
    private const int WebSearchMaxUses = 8;

    private const string ExtractionInstruction =
        "You are a visual-development analyst preparing a graphic-novel design bible. Read the novel " +
        "excerpt and list every distinct CHARACTER and every distinct SETTING/LOCATION that appears, " +
        "with concrete visual details useful for an illustrator (appearance, clothing, age, build for " +
        "characters; architecture, era, mood, palette for settings). For each entity also give its " +
        "approximate physical SCALE so the illustrations stay proportionate: for a character an " +
        "approximate height and build relative to an average adult; for a setting its approximate size " +
        "and any notable scale cues. Respond with ONLY a JSON object: " +
        "{\"characters\":[{\"name\":string,\"description\":string,\"scale\":string,\"aliases\":[string]}]," +
        "\"settings\":[{\"name\":string,\"description\":string,\"scale\":string,\"aliases\":[string]}]}. " +
        "Use [] when none are present, and \"\" for an unknown scale. No prose outside the JSON.";

    private const string StoryboardInstruction =
        "You are a comics writer adapting a novel into a graphic novel. Using the supplied design bible " +
        "for consistency, break the provided passage into volumes, pages, and panels — but be " +
        "ECONOMICAL. This graphic novel is a comprehension aid: draw only the beats a reader needs to " +
        "follow WHAT happens, WHO does it, and WHERE it happens. Each panel must carry a distinct, " +
        "load-bearing action; SKIP transitional, in-between, establishing-only, and reaction-only " +
        "moments that don't advance the action, and skip a beat already clear from an adjacent panel. " +
        "Prefer the fewest panels that keep the action legible over a dense shot-by-shot breakdown. " +
        "Aim for 1 to 4 panels per page (never more than 6), and give a pivotal " +
        "beat its own page. For each panel give a vivid visual prompt that captures a SPECIFIC MOMENT " +
        "OF ONGOING ACTION — the characters caught mid-gesture or mid-movement, interacting with each " +
        "other and the setting — with an explicit shot type or camera angle (e.g. wide establishing " +
        "shot, low-angle close-up, over-the-shoulder); avoid static, posed, portrait, or " +
        "looking-at-camera framing. Respect the relative scale of the entities given in the design " +
        "bible. Put no lettering in the art; give any dialogue and caption to be typeset separately, " +
        "and the names of design-bible entities present. Give each panel a shotType of \"standard\", " +
        "\"wide\" (a full-width establishing/landscape shot), or \"splash\" (a full-page moment reserved " +
        "for a genuinely pivotal beat — use sparingly). Maintain continuity with the running summary. " +
        "Set dialogueKind to \"thought\" only when the dialogue is a character's unspoken interior " +
        "thought (it letters as a thought balloon); otherwise \"speech\". Set sfx to a short onomatopoeia " +
        "for a prominent sound in the panel (e.g. \"CRASH\", \"BOOM\", \"KNOCK\") in the book's language, " +
        "or null when there is no notable sound — which is most panels. " +
        "Mark scene boundaries: set newScene to true on a panel that BEGINS A NEW SCENE — a change of " +
        "location, a jump in time, or a distinct dramatic unit — and false on a panel that continues the " +
        "current scene. The very first panel of the passage begins a scene. These groupings are used to " +
        "render panels efficiently, so place them at genuine story boundaries. " +
        "Respond with ONLY a JSON object: " +
        "{\"continuitySummary\":string,\"volumes\":[{\"number\":int,\"title\":string,\"pages\":" +
        "[{\"chapterNumber\":int,\"panels\":[{\"visualPrompt\":string,\"dialogue\":string|null," +
        "\"caption\":string|null,\"referenceNames\":[string],\"shotType\":\"standard\"|\"wide\"|\"splash\"," +
        "\"dialogueKind\":\"speech\"|\"thought\",\"sfx\":string|null,\"newScene\":bool}]}]}]}." +
        " No prose outside the JSON.";

    private const string ArtStyleInstruction =
        "You are an art director choosing the visual style for a graphic-novel adaptation. From the prose " +
        "sample and the cast below, infer the book's genre, tone, period and setting, then describe ONE " +
        "cohesive illustration style for the WHOLE book — medium, rendering, linework, colour palette, " +
        "lighting and mood — in a single vivid sentence a comics illustrator could follow consistently. " +
        "Respond with ONLY a JSON object: {\"artStyle\":string}. No prose outside the JSON.";

    private const string ChapterAnalysisInstruction =
        "You are a developmental editor analyzing ONE excerpt of a novel manuscript to help the author " +
        "revise toward publication. Use the KNOWN CAST names consistently when they appear. LOCATION IN " +
        "THE BOOK names the chapter(s) this excerpt covers — every observation you make is about that " +
        "part of the book, so keep each one specific enough to be found there again. Read the " +
        "excerpt and record concise, concrete, accurate observations. Respond with ONLY a JSON object: " +
        "{\"charactersPresent\":[string],\"keyEvents\":[string],\"characterBeats\":[string]," +
        "\"setupsAndPayoffs\":[string],\"possibleInconsistencies\":[string],\"factualClaims\":[string]," +
        "\"notableStrengths\":[string],\"notableWeaknesses\":[string],\"lineEditCandidates\":[string]," +
        "\"proseObservations\":[string],\"dialogueObservations\":[string],\"sceneBeats\":[string]}. " +
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
        "Use [] when none are present. Give AT MOST 12 items in each list, the most significant ones. " +
        "No prose outside the JSON.";

    private const string ConsolidationInstruction =
        "You are a panel of professional readers producing the STORY half of a REVISION REPORT for an " +
        "author preparing a novel for publication. You are given per-chunk notes gathered across the " +
        "whole manuscript in reading order, plus the cast. Each note carries the chapter it was taken " +
        "from. Synthesize the WHOLE book (do not merely concatenate the notes); " +
        "be accurate, fair, specific, and constructive. Respond with ONLY a JSON object: " +
        "{\"logline\":string,\"synopsis\":string,\"characters\":[{\"name\":string,\"role\":string," +
        "\"arcSummary\":string,\"relationships\":[string]}],\"themes\":[string],\"plotHoles\":[FINDING]," +
        "\"inconsistencies\":[FINDING],\"inaccuracies\":[FINDING],\"pacingNotes\":[FINDING],\"reviews\":" +
        "[{\"perspective\":string,\"assessment\":string,\"strengths\":[string],\"weaknesses\":[string]," +
        "\"recommendation\":string}],\"revisionPlan\":[{\"priority\":string,\"chapter\":string," +
        "\"issue\":string,\"suggestedFix\":string}],\"comparableTitles\":[string]," +
        "\"marketPositioning\":string}. " +
        // Every finding has to be findable: an observation the author can't locate in the manuscript
        // costs them a re-read to act on. The chapter is already on the notes, so it only has to be
        // carried through rather than worked out.
        "FINDING is an object {\"chapter\":string,\"detail\":string}: copy \"chapter\" from the note the " +
        "observation came from (\"Chapter 7\", \"Chapters 7–8\"), naming the EARLIEST chapter involved " +
        "when an observation spans several, and use \"\" only when the notes carry no chapter at all. " +
        "Set \"chapter\" on each revisionPlan item the same way. " +
        "Provide AT LEAST THREE reviews from DISTINCT, fair perspectives (e.g. developmental editor, " +
        "target-genre reader, literary critic, acquiring agent) — differing but honest, each with real " +
        "strengths and weaknesses. Order revisionPlan HIGHEST-IMPACT FIRST and set priority to " +
        "\"High\", \"Medium\", or \"Low\". Use [] when none. No prose outside the JSON.";

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

    private const string FactCheckInstruction =
        "You are a fact-checker for a novel heading toward publication. You are given the checkable " +
        "real-world statements the manuscript makes, each with the chapter it came from. SEARCH THE WEB to " +
        "verify each one, then report what you found. Respond with ONLY a JSON object: " +
        "{\"checks\":[{\"claim\":string,\"verdict\":string,\"note\":string,\"chapter\":string}]}. " +
        "verdict is EXACTLY one of \"Correct\", \"Incorrect\", \"Partly correct\", or \"Unverified\". Use " +
        "\"Unverified\" whenever the search does not settle it — a confident \"Incorrect\" on something you " +
        "could not actually check sends the author chasing a problem that isn't there. note says what you " +
        "found and, for anything not Correct, what the accurate version is. Copy \"chapter\" from the claim " +
        "you were given. Judge only real-world accuracy: invented places, people and history in a work of " +
        "fiction are not errors, so leave them out entirely rather than reporting them as Incorrect. " +
        "Report at most 30 checks, the ones that most matter. No prose outside the JSON.";

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

    private const string ImplyInstruction =
        "You are a comics director revising a single panel's visual prompt. The image model refused to " +
        "draw it because some element is too explicit to depict. Rewrite the prompt so that same beat is " +
        "IMPLIED rather than shown: turn the camera away, cut to reaction or aftermath, obscure the " +
        "explicit element with foreground/shadow/framing, or move it just off-panel — while keeping the " +
        "mood, characters, and story moment intact. Keep it a single vivid panel description with no " +
        "lettering. Respond with ONLY the rewritten visual prompt as plain text — no preamble, no quotes.";

    private readonly HttpClient httpClient;
    private readonly ClaudeOptions options;

    public ClaudeAnalysisClient(HttpClient httpClient, IOptions<ClaudeOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public async Task<DesignBibleFragment> ExtractEntities(string chunkText, CancellationToken cancellationToken)
    {
        // Fast tier: one call per chunk, and the job is extraction against a fixed JSON contract.
        var reply = await SendMessage(ExtractionInstruction, chunkText, cancellationToken, model: options.FastModel);
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

    public async Task<StoryboardSegment> Storyboard(
        string textWindow, string continuitySummary, DesignBible bible, string artStyle,
        CancellationToken cancellationToken)
    {
        var bibleJson = JsonSerializer.Serialize(bible, JsonResponse.CaseInsensitive);
        // The art style and design bible are identical for every window of a novel, so they go in a
        // cached block: consecutive windows and any retries then bill this as a cache read (~0.1x)
        // rather than re-sending the whole bible at full input price on every call.
        var cachedContext =
            $"ART STYLE (apply to every visual prompt): {artStyle}\n\n" +
            $"DESIGN BIBLE (JSON):\n{bibleJson}";
        var userContent =
            $"CONTINUITY SO FAR:\n{(string.IsNullOrWhiteSpace(continuitySummary) ? "(none yet)" : continuitySummary)}\n\n" +
            $"PASSAGE TO ADAPT:\n{textWindow}";

        var reply = await SendMessage(
            StoryboardInstruction, userContent, cancellationToken,
            maxTokens: options.StoryboardMaxTokens, cachedContext: cachedContext);
        var json = JsonResponse.ExtractObject(reply)
            ?? throw new InvalidOperationException("Claude storyboard reply did not contain a JSON object.");

        var dto = JsonResponse.Deserialize<StoryboardDto>(json)
            ?? throw new InvalidOperationException("Claude storyboard reply could not be parsed.");

        var volumes = (dto.Volumes ?? [])
            .Select(v => new VolumeDraft(
                v.Number,
                v.Title ?? string.Empty,
                (v.Pages ?? []).Select(MapPage).ToList()))
            .ToList();

        return new StoryboardSegment(volumes, dto.ContinuitySummary ?? string.Empty);
    }

    public async Task<ChapterNotes> AnalyzeChapter(
        string chapterText, string chapterLabel, IReadOnlyList<string> characterNames,
        CancellationToken cancellationToken)
    {
        var cast = characterNames.Count == 0 ? "(none identified yet)" : string.Join(", ", characterNames);
        var location = string.IsNullOrWhiteSpace(chapterLabel) ? "(unknown)" : chapterLabel;
        var userContent = $"LOCATION IN THE BOOK: {location}\n\nKNOWN CAST: {cast}\n\nEXCERPT:\n{chapterText}";

        // Fast tier: one call per chunk, recording observations against a fixed JSON contract. The
        // judgement calls — synthesising these notes into a report — stay on the strong model.
        var reply = await SendMessage(
            ChapterAnalysisInstruction, userContent, cancellationToken, options.ChapterNotesMaxTokens,
            model: options.FastModel);
        var json = JsonResponse.ExtractObject(reply);
        // The chapter is known exactly from the chunk's offsets, so it is stamped on here rather than
        // trusted to the model — even a reply that failed to parse still carries where it came from.
        var located = ChapterNotes.Empty with { Chapter = chapterLabel };
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
            dto.DialogueObservations ?? [], dto.SceneBeats ?? []);
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
            (dto.Reviews ?? []).Select(r =>
                new Review(r.Perspective ?? string.Empty, r.Assessment ?? string.Empty, r.Strengths ?? [], r.Weaknesses ?? [], r.Recommendation ?? string.Empty)).ToList(),
            (dto.RevisionPlan ?? []).Select(r =>
                new RevisionItem(r.Priority ?? string.Empty, r.Chapter ?? string.Empty,
                    r.Issue ?? string.Empty, r.SuggestedFix ?? string.Empty)).ToList(),
            dto.ComparableTitles ?? [],
            dto.MarketPositioning ?? string.Empty,
            // The craft sections come from their own call (ConsolidateCraft) and are folded in by the
            // caller; this half of the report leaves them empty rather than half-answering them.
            CraftReport.Empty.LineEdits, CraftReport.Empty.ProseNotes,
            CraftReport.Empty.DialogueNotes, CraftReport.Empty.SceneBreakdown);
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

    public async Task<IReadOnlyList<FactCheck>> CheckFacts(
        IReadOnlyList<ChapterNotes> notes, CancellationToken cancellationToken)
    {
        // Only the claims, each tagged with where it came from. Sending the whole notes payload would
        // bury the task and invite the model to fact-check the fiction itself.
        var claims = notes
            .SelectMany(n => n.FactualClaims.Select(claim => new { chapter = n.Chapter, claim }))
            .Where(c => !string.IsNullOrWhiteSpace(c.claim))
            .ToList();

        // Nothing checkable in the whole book is a normal outcome for some fiction — don't spend a call
        // (or a search) to confirm an empty list.
        if (claims.Count == 0)
            return Array.Empty<FactCheck>();

        var userContent = "CLAIMS TO CHECK (JSON):\n" + JsonSerializer.Serialize(claims, JsonResponse.CaseInsensitive);

        var reply = await SendMessage(
            FactCheckInstruction, userContent, cancellationToken, options.AnalysisMaxTokens, webSearch: true);
        var json = JsonResponse.ExtractObject(reply);
        if (json is null)
            return Array.Empty<FactCheck>();

        var dto = JsonResponse.Deserialize<FactCheckReplyDto>(json);
        return (dto?.Checks ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Claim))
            .Select(c => new FactCheck(
                c.Claim!, string.IsNullOrWhiteSpace(c.Verdict) ? "Unverified" : c.Verdict!,
                c.Note ?? string.Empty, c.Chapter ?? string.Empty))
            .ToList();
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

    public async Task<string> RewritePromptToImply(string visualPrompt, CancellationToken cancellationToken)
    {
        var reply = await SendMessage(ImplyInstruction, visualPrompt, cancellationToken);
        var rewritten = reply.Trim();
        // Fall back to the original prompt if the model returned nothing usable, so the caller always
        // has a prompt to try rather than an empty one.
        return string.IsNullOrWhiteSpace(rewritten) ? visualPrompt : rewritten;
    }

    // model selects the tier: null means the strong default. Only the mechanical per-chunk calls pass the
    // fast one — see ClaudeOptions.FastModel for why those two and not the rest.
    private async Task<string> SendMessage(
        string instruction, string userText, CancellationToken cancellationToken,
        int? maxTokens = null, string? cachedContext = null, string? model = null, bool webSearch = false)
    {
        // Invariant, reused context (the design bible) is cached as its own block so repeated calls
        // within the cache window bill it as a cache read instead of full input. Both blocks share one
        // anonymous shape so the array stays strongly typed (no object-typed JSON serialization).
        var system = string.IsNullOrEmpty(cachedContext)
            ? new[]
            {
                new { type = "text", text = instruction, cache_control = new { type = "ephemeral" } }
            }
            : new[]
            {
                new { type = "text", text = instruction, cache_control = new { type = "ephemeral" } },
                new { type = "text", text = cachedContext, cache_control = new { type = "ephemeral" } }
            };

        var effectiveMaxTokens = maxTokens ?? options.MaxTokens;
        var payload = new
        {
            model = string.IsNullOrWhiteSpace(model) ? options.Model : model,
            max_tokens = effectiveMaxTokens,
            stream = true,
            system,
            messages = new[]
            {
                new { role = "user", content = userText }
            }
        };

        // Serialized through a node so the optional server-side tool can be attached without a second
        // payload shape. Property names are already snake_case on the anonymous type, so they survive
        // as written.
        var requestBody = JsonSerializer.SerializeToNode(payload)!.AsObject();
        if (webSearch)
        {
            // Anthropic's hosted web-search tool: the search runs server-side and the model keeps
            // generating, so the existing text accumulation needs no special handling. Capped because
            // each search bills, and the fact-check has a bounded list of claims to settle.
            requestBody["tools"] = new JsonArray(new JsonObject
            {
                ["type"] = WebSearchToolType,
                ["name"] = "web_search",
                ["max_uses"] = WebSearchMaxUses
            });
        }

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
            throw ToException(response, body);
        }

        return await ReadStreamedText(response, effectiveMaxTokens, cancellationToken);
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

    private static PageDraft MapPage(PageDto page) =>
        new(page.ChapterNumber,
            (page.Panels ?? [])
                .Select(p => new PanelDraft(
                    p.VisualPrompt ?? string.Empty, p.Dialogue, p.Caption, p.ReferenceNames ?? [],
                    ParseShot(p.ShotType), ParseDialogueKind(p.DialogueKind),
                    string.IsNullOrWhiteSpace(p.Sfx) ? null : p.Sfx!.Trim(), p.NewScene))
                .ToList());

    // Maps the model's dialogueKind string to the enum, defaulting to Speech for anything unrecognized or
    // absent (an older reply, a typo) — so a missing value simply letters as a normal spoken balloon.
    private static DialogueKind ParseDialogueKind(string? kind) =>
        kind?.Trim().ToLowerInvariant() == "thought" ? DialogueKind.Thought : DialogueKind.Speech;

    // Maps the model's shotType string to the enum, defaulting to Standard for anything unrecognized or
    // absent (an older reply, a typo, or a shot vocabulary we don't lay out specially) — so a missing or
    // odd value simply lays the panel out normally rather than failing the parse.
    private static ShotType ParseShot(string? shot) => shot?.Trim().ToLowerInvariant() switch
    {
        "wide" => ShotType.Wide,
        "splash" => ShotType.Splash,
        _ => ShotType.Standard
    };

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

    private sealed class StoryboardDto
    {
        public string? ContinuitySummary { get; set; }
        public List<VolumeDto>? Volumes { get; set; }
    }

    private sealed class VolumeDto
    {
        public int Number { get; set; }
        public string? Title { get; set; }
        public List<PageDto>? Pages { get; set; }
    }

    private sealed class PageDto
    {
        public int ChapterNumber { get; set; }
        public List<PanelDto>? Panels { get; set; }
    }

    private sealed class PanelDto
    {
        public string? VisualPrompt { get; set; }
        public string? Dialogue { get; set; }
        public string? Caption { get; set; }
        public List<string>? ReferenceNames { get; set; }
        public string? ShotType { get; set; }
        public string? DialogueKind { get; set; }
        public string? Sfx { get; set; }
        public bool NewScene { get; set; }
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
        public List<Finding>? PlotHoles { get; set; }
        public List<Finding>? Inconsistencies { get; set; }
        public List<Finding>? Inaccuracies { get; set; }
        public List<Finding>? PacingNotes { get; set; }
        public List<ReviewDto>? Reviews { get; set; }
        public List<RevisionItemDto>? RevisionPlan { get; set; }
        public List<string>? ComparableTitles { get; set; }
        public string? MarketPositioning { get; set; }
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

    private sealed class ReviewDto
    {
        public string? Perspective { get; set; }
        public string? Assessment { get; set; }
        public List<string>? Strengths { get; set; }
        public List<string>? Weaknesses { get; set; }
        public string? Recommendation { get; set; }
    }

    private sealed class RevisionItemDto
    {
        public string? Priority { get; set; }
        public string? Chapter { get; set; }
        public string? Issue { get; set; }
        public string? SuggestedFix { get; set; }
    }

    private sealed class FactCheckReplyDto
    {
        public List<FactCheckDto>? Checks { get; set; }
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

    private sealed class FactCheckDto
    {
        public string? Claim { get; set; }
        public string? Verdict { get; set; }
        public string? Note { get; set; }
        public string? Chapter { get; set; }
    }
}
