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

    // Literary-analysis "reduce": consolidates all chunk notes into the whole revision report
    // (character map + arcs, plot holes, inconsistencies/inaccuracies, multi-perspective reviews,
    // a prioritized revision plan, and publish-oriented positioning).
    Task<BookAnalysisResult> ConsolidateAnalysis(
        string title,
        IReadOnlyList<ChapterNotes> notes,
        DesignBible bible,
        string continuitySummary,
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
