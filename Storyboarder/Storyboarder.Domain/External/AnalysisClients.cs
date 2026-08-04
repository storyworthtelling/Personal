using Storyboarder.Domain.Analysis;

namespace Storyboarder.Domain.External;

// Literary analysis via Claude: per-chunk entity extraction and windowed storyboarding.
// (Bible consolidation is done deterministically by the Domain DesignBibleMerger, not the model.)
// Implementations raise RateLimitException on provider throttling.
public interface IClaudeAnalysisClient
{
    Task<DesignBibleFragment> ExtractEntities(string chunkText, CancellationToken cancellationToken);

    Task<StoryboardSegment> Storyboard(
        string textWindow,
        string continuitySummary,
        DesignBible bible,
        string artStyle,
        CancellationToken cancellationToken);

    // Derives one cohesive illustration style for the whole book from a sample of its prose and its
    // cast, so the art matches the manuscript's genre and tone instead of a generic default. Returns a
    // short descriptor to prepend to every panel prompt (empty if it can't be derived, in which case the
    // caller keeps the default style — art direction must never stall the conversion).
    Task<string> DeriveArtStyle(
        string sampleText,
        DesignBible bible,
        CancellationToken cancellationToken);

    // Describes a single comic-book cover illustration for one volume, from an excerpt of the chapter it
    // covers. Deliberately spoiler-free: it evokes the section's mood and iconography without revealing
    // twists or endings. Returns a plain-text visual prompt for the image model (no lettering — the novel
    // and chapter names are typeset over the art separately), or empty when it can't be derived, so a
    // failed cover concept degrades to the plain typographic cover rather than blocking the book.
    Task<string> DescribeVolumeCover(
        string novelTitle,
        string chapterTitle,
        string chapterExcerpt,
        string artStyle,
        CancellationToken cancellationToken);

    // Literary-analysis "map": observations for one manuscript chunk (character beats, events,
    // possible inconsistencies, checkable claims, strengths/weaknesses) anchored to the known cast.
    // chapterLabel names the chapter(s) the chunk covers ("Chapter 7", "Chapters 7–8"); it is given to
    // the model as context AND stamped onto the returned notes, so the consolidated report can cite a
    // chapter for every finding. Pass an empty label when the chapters aren't known.
    Task<ChapterNotes> AnalyzeChapter(
        string chapterText,
        string chapterLabel,
        IReadOnlyList<string> characterNames,
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

    // Puts the manuscript's checkable real-world statements to a web search and reports what it found.
    // Separate from the other reduce calls because it is the only one granted a search tool: the rest
    // must reason from the manuscript alone, not from whatever the web says the book should contain.
    Task<IReadOnlyList<FactCheck>> CheckFacts(
        IReadOnlyList<ChapterNotes> notes,
        CancellationToken cancellationToken);

    // Reads a representative prose sample from each chapter and reports where the author's own writing
    // drifts from the book's dominant authorial voice. Its own call because it needs actual prose in front
    // of the model (register, diction, cadence can't be judged from the per-chunk notes the other reduces
    // work from). Returns the dominant-voice characterization plus the divergent passages.
    Task<VoiceConsistency> AssessVoiceConsistency(
        IReadOnlyList<ChapterSample> samples,
        CancellationToken cancellationToken);

    // Restages a panel's visual prompt so objectionable content is implied — occluded, off-frame, or
    // cut away from — rather than depicted. Used only when even a relaxed render hit the image model's
    // non-adjustable floor, to salvage the beat without showing what the model refuses to draw.
    Task<string> RewritePromptToImply(string visualPrompt, CancellationToken cancellationToken);
}

// Image generation via Gemini. Reference sheets are generated once per design-bible entity, then
// supplied back as reference images on each panel render to keep characters/settings consistent.
public interface IGeminiImageClient
{
    Task<byte[]> GenerateReferenceSheet(string description, string artStyle, CancellationToken cancellationToken);

    // aspectRatio (width/height of the panel's slot on the page) is snapped to the nearest ratio the
    // image model supports and requested, so the ~1MP output fills the panel instead of being
    // letterboxed. Null requests the model's default framing.
    Task<byte[]> GeneratePanel(
        string visualPrompt,
        string artStyle,
        IReadOnlyList<byte[]> referenceImages,
        double? aspectRatio,
        CancellationToken cancellationToken);

    // The user-approved re-render of a safety-blocked panel: identical to GeneratePanel except that the
    // given HARM_CATEGORY_* names are relaxed (BLOCK_NONE) for this one call. The app's default
    // thresholds are untouched everywhere else. An empty list behaves like the default render.
    Task<byte[]> GeneratePanel(
        string visualPrompt,
        string artStyle,
        IReadOnlyList<byte[]> referenceImages,
        double? aspectRatio,
        IReadOnlyCollection<string> relaxedCategories,
        CancellationToken cancellationToken);
}

// Fetches the plain text of a Google Doc selected via the in-app Drive picker, using the
// browser-granted OAuth access token.
public interface IGoogleDocsClient
{
    Task<string> FetchText(string documentId, string accessToken, CancellationToken cancellationToken);
}
