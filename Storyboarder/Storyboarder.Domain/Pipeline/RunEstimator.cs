namespace Storyboarder.Domain.Pipeline;

// What a conversion is expected to cost before it is started, so a book that would take three weeks of
// daily quota isn't discovered three weeks in.
public sealed record RunEstimate(
    int Chunks,
    int ClaudeCalls,
    int Panels,
    int ImageCalls,
    TimeSpan Duration,
    // Which provider's quota sets the duration, so the sentence can say what to raise to go faster.
    string LimitingFactor);

// Projects the size and duration of a conversion from the manuscript's length.
//
// The counts come from the pipeline's own arithmetic (chunk size, storyboard window size), so they are
// exact given the text. The panel count cannot be — only storyboarding knows how many beats a book has —
// so it is extrapolated from the owner's own converted novel and is explicitly an approximation.
//
// Duration is deliberately derived from the configured RATE LIMITS rather than from how long a call takes:
// on a real book the run is not compute-bound, it is quota-bound. The owner's last novel needed 2162 panel
// renders against a default ceiling of 100 image calls a day — three weeks of wall-clock that no estimate
// based on per-call latency would ever have revealed.
public static class RunEstimator
{
    // One panel per this many characters of manuscript, taken from the owner's converted novel: ~432K
    // characters (9 chunks at the 48K target) became 2162 panels. A rough anchor, not a law — a
    // dialogue-heavy book yields more panels per character than a descriptive one.
    private const int CharactersPerPanel = 200;

    // Claude calls that are not per-chunk: the two report consolidations (story half and craft half).
    private const int FixedClaudeCalls = 2;

    public static RunEstimate Estimate(
        int characterCount,
        bool reportOnly,
        int chunkChars,
        int storyboardWindowChars,
        int claudeCallsPerDay,
        int imageCallsPerDay,
        int referenceSheets)
    {
        if (characterCount < 0)
            throw new ArgumentOutOfRangeException(nameof(characterCount), "A manuscript cannot have negative length.");

        var chunks = DivideUp(characterCount, chunkChars);

        // Per chunk: one entity extraction and one set of analysis notes. Storyboarding runs on much
        // smaller windows, one Claude call each, and a report-only draft skips it entirely.
        var storyboardWindows = reportOnly ? 0 : DivideUp(characterCount, storyboardWindowChars);
        var claudeCalls = (chunks * 2) + storyboardWindows + FixedClaudeCalls;

        var panels = reportOnly ? 0 : DivideUp(characterCount, CharactersPerPanel);
        var imageCalls = reportOnly ? 0 : panels + referenceSheets;

        // Whichever provider's daily ceiling takes longest to drain sets the wall-clock.
        var claudeDays = Ratio(claudeCalls, claudeCallsPerDay);
        var imageDays = Ratio(imageCalls, imageCallsPerDay);
        var limiting = imageDays >= claudeDays ? "image generation" : "text generation";
        var duration = TimeSpan.FromDays(Math.Max(claudeDays, imageDays));

        return new RunEstimate(chunks, claudeCalls, panels, imageCalls, duration, limiting);
    }

    private static int DivideUp(int total, int per) =>
        per <= 0 ? 0 : (int)Math.Ceiling(total / (double)per);

    private static double Ratio(int calls, int perDay) =>
        perDay <= 0 ? 0 : calls / (double)perDay;
}
