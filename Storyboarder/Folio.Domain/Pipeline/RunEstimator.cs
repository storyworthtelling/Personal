namespace Folio.Domain.Pipeline;

// What a review is expected to cost before it is started, so a book that would take days of daily quota
// isn't discovered part-way through.
public sealed record RunEstimate(
    int Chunks,
    int ClaudeCalls,
    int Scenes,
    int ImageCalls,
    TimeSpan Duration,
    // Which provider's quota sets the duration, so the sentence can say what to raise to go faster.
    string LimitingFactor);

// Projects the size and duration of a review from the manuscript's length.
//
// The chunk and Claude-call counts come from the pipeline's own arithmetic, so they are exact given the
// text. The scene count cannot be — only scene detection knows how many scenes a book has — so it is
// extrapolated from an average scene length and is explicitly an approximation.
//
// Duration is deliberately derived from the configured RATE LIMITS rather than from how long a call takes:
// on a real book the run is quota-bound, not compute-bound.
public static class RunEstimator
{
    // One evidence image per this many characters of manuscript — an average scene length. A rough anchor
    // (a ~150K-word novel of ~750K characters lands around 250 scenes), not a law.
    private const int CharactersPerScene = 3000;

    // Claude calls that are not per-chunk: the two report consolidations (story half and craft half) plus
    // the voice-consistency read.
    private const int FixedClaudeCalls = 3;

    public static RunEstimate Estimate(
        int characterCount,
        int chunkChars,
        int claudeCallsPerDay,
        int imageCallsPerDay,
        int referenceSheets)
    {
        if (characterCount < 0)
            throw new ArgumentOutOfRangeException(nameof(characterCount), "A manuscript cannot have negative length.");

        var chunks = DivideUp(characterCount, chunkChars);

        // Per chunk: one entity extraction and one set of analysis notes, plus the fixed reduce calls.
        var claudeCalls = (chunks * 2) + FixedClaudeCalls;

        // One wordless evidence image per scene, plus the recurring-entity reference sheets.
        var scenes = DivideUp(characterCount, CharactersPerScene);
        var imageCalls = scenes + referenceSheets;

        // Whichever provider's daily ceiling takes longest to drain sets the wall-clock.
        var claudeDays = Ratio(claudeCalls, claudeCallsPerDay);
        var imageDays = Ratio(imageCalls, imageCallsPerDay);
        var limiting = imageDays >= claudeDays ? "image generation" : "text generation";
        var duration = TimeSpan.FromDays(Math.Max(claudeDays, imageDays));

        return new RunEstimate(chunks, claudeCalls, scenes, imageCalls, duration, limiting);
    }

    private static int DivideUp(int total, int per) =>
        per <= 0 ? 0 : (int)Math.Ceiling(total / (double)per);

    private static double Ratio(int calls, int perDay) =>
        perDay <= 0 ? 0 : calls / (double)perDay;
}
