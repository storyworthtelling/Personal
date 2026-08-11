using Storyboarder.Domain.Analysis;

namespace Storyboarder.Domain.External;

// Literary analysis via Claude: per-chunk entity extraction and the map/reduce of the revision report.
// (Bible consolidation is done deterministically by the Domain DesignBibleMerger, not the model.)
// Implementations raise RateLimitException on provider throttling.
public interface IClaudeAnalysisClient
{
    Task<DesignBibleFragment> ExtractEntities(string chunkText, CancellationToken cancellationToken);

    // Derives one cohesive illustration style for the whole book from a sample of its prose and its
    // cast, so the per-scene evidence images match the manuscript's genre and tone instead of a generic
    // default. Returns a short descriptor to prepend to every scene-image prompt (empty if it can't be
    // derived, in which case the caller keeps the default style — art direction must never stall the review).
    Task<string> DeriveArtStyle(
        string sampleText,
        DesignBible bible,
        CancellationToken cancellationToken);

    // Describes ONE wordless illustration of a scene's emotional peak — the beat the prose is measured
    // against in the fidelity check. Given the scene's text and the cast/settings, it returns a plain-text
    // visual prompt for the image model (no lettering). Best-effort: empty when it can't be derived, so a
    // scene image is never blocked on art direction. `avoidMoments` names beats already tried and refused,
    // so a retry can target a different moment of the same scene.
    Task<string> DescribeSceneImage(
        string sceneText,
        DesignBible bible,
        string artStyle,
        IReadOnlyList<string> avoidMoments,
        CancellationToken cancellationToken);

    // Literary-analysis "map": observations for one manuscript chunk (character beats, events,
    // possible inconsistencies, checkable claims, strengths/weaknesses) anchored to the known cast.
    // chapterLabel names the chapter(s) the chunk covers ("Chapter 7", "Chapters 7–8"); it is given to
    // the model as context AND stamped onto the returned notes, so the consolidated report can cite a
    // chapter for every finding. Pass an empty label when the chapters aren't known. priorRunningState is
    // the cumulative "story so far" from the preceding chunk (empty for the first); the returned notes carry
    // it forward updated, so long-range continuity and setup/payoff survive the chunk-by-chunk map.
    Task<ChapterNotes> AnalyzeChapter(
        string chapterText,
        string chapterLabel,
        IReadOnlyList<string> characterNames,
        string priorRunningState,
        CancellationToken cancellationToken);

    // Literary-analysis "reduce", story half: consolidates all chunk notes into the story report
    // (character map + arcs, plot holes, inconsistencies/inaccuracies, multi-perspective reviews,
    // a prioritized revision plan, and publish-oriented positioning). The craft sections come back
    // empty here — see ConsolidateCraft.
    Task<BookAnalysisResult> ConsolidateAnalysis(
        string title,
        IReadOnlyList<ChapterNotes> notes,
        DesignBible bible,
        string continuitySummary,
        CancellationToken cancellationToken);

    // Literary-analysis "reduce", craft half: line-level edits, prose style & voice, dialogue
    // critique, and the scene-by-scene breakdown. A SECOND call rather than more sections on the
    // first: together they are more output than one call can produce without truncating, and a single
    // reply that long streams for long enough that the background worker is killed mid-flight. The
    // caller folds the result into the story report with BookAnalysisResult.WithCraft.
    Task<CraftReport> ConsolidateCraft(
        string title,
        IReadOnlyList<ChapterNotes> notes,
        CancellationToken cancellationToken);

    // Reads a representative prose sample from each chapter and reports where the author's own writing
    // drifts from the book's dominant authorial voice. Its own call because it needs actual prose in front
    // of the model (register, diction, cadence can't be judged from the per-chunk notes the other reduces
    // work from). Returns the dominant-voice characterization plus the divergent passages.
    Task<VoiceConsistency> AssessVoiceConsistency(
        IReadOnlyList<ChapterSample> samples,
        CancellationToken cancellationToken);

    // Cross-draft finding reconciliation: given the prior draft's finding threads and this draft's freshly
    // consolidated findings, decides which current finding is the SAME underlying issue as which prior
    // thread — even after the wording, phrasing, or chapter has moved — so a finding keeps a stable identity
    // across drafts. Returns one match per candidate (a prior ThreadId, or null when it is a new issue).
    // Best-effort: a blank/partial reply degrades to "everything is new", so reconciliation never blocks
    // the report. FindingReconciler applies these decisions to produce the persisted occurrence rows.
    Task<FindingReconciliation> ReconcileFindings(
        IReadOnlyList<PriorFinding> priorThreads,
        IReadOnlyList<CandidateFinding> candidates,
        CancellationToken cancellationToken);

    // The additive read (Phase 3): from the per-chunk notes (their summarized moments, off-page events, thin
    // spots, character beats, and setups), the running story-state, the manuscript's actual word count, and
    // the inferred genre, propose the scenes/beats to ADD (ranked by impact), the two-sided show-vs-tell
    // notes (dramatize the summarized, tighten the over-shown), and the genre-norm length range. It NUDGES,
    // never writes — pointers only, never drafted prose. Best-effort: a blank/partial reply degrades to
    // ExpansionReport.Empty so the additive read never blocks the rest of the report.
    Task<ExpansionReport> ProposeExpansion(
        string title,
        IReadOnlyList<ChapterNotes> notes,
        string continuitySummary,
        int wordCount,
        string genre,
        CancellationToken cancellationToken);
}

// Image generation via Gemini. Reference sheets are generated once per recurring design-bible entity,
// then supplied back as reference images on each scene render to keep characters/settings consistent —
// coherence is part of the fidelity check.
public interface IGeminiImageClient
{
    Task<byte[]> GenerateReferenceSheet(string description, string artStyle, CancellationToken cancellationToken);

    // Renders one wordless evidence image for a scene's emotional peak. aspectRatio (width/height of the
    // image's slot in the report) is snapped to the nearest ratio the image model supports and requested,
    // so the ~1MP output fills the slot instead of being letterboxed. Null requests the model's default
    // (portrait) framing. Reference images keep recurring characters/settings on-model.
    Task<byte[]> GenerateSceneImage(
        string visualPrompt,
        string artStyle,
        IReadOnlyList<byte[]> referenceImages,
        double? aspectRatio,
        CancellationToken cancellationToken);
}

// Fetches the plain text of a source document (Google Doc or a supported uploaded format) selected via
// the in-app Drive picker, using the browser-granted OAuth access token.
public interface IGoogleDocsClient
{
    Task<string> FetchText(string documentId, string accessToken, CancellationToken cancellationToken);
}
