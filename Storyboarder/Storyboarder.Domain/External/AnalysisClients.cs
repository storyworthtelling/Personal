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
}

// Image generation via Gemini. Reference sheets are generated once per design-bible entity, then
// supplied back as reference images on each panel render to keep characters/settings consistent.
public interface IGeminiImageClient
{
    Task<byte[]> GenerateReferenceSheet(string description, string artStyle, CancellationToken cancellationToken);

    Task<byte[]> GeneratePanel(
        string visualPrompt,
        string artStyle,
        IReadOnlyList<byte[]> referenceImages,
        CancellationToken cancellationToken);
}

// Fetches the plain text of a Google Doc selected via the in-app Drive picker, using the
// browser-granted OAuth access token.
public interface IGoogleDocsClient
{
    Task<string> FetchText(string documentId, string accessToken, CancellationToken cancellationToken);
}
