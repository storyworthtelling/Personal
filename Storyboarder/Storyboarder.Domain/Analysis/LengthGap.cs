namespace Storyboarder.Domain.Analysis;

// The plain-language framing of a manuscript's length against its genre norm — the line that opens the
// report, since the owner's headline need is closing a word-count gap. Pure and deterministic (the model
// supplies the genre range; the count is measured), so it is unit-tested without a call.
public static class LengthGap
{
    // Below the range → how far short; within → "within the norm"; above → how far over. A missing or
    // inverted range degrades to a bare count (no norm claim); no word count yields an empty line.
    public static string Describe(int wordCount, int targetLow, int targetHigh)
    {
        if (wordCount <= 0)
            return string.Empty;

        var count = $"{wordCount:N0} words";
        var hasRange = targetLow > 0 && targetHigh >= targetLow;
        if (!hasRange)
            return count;

        var range = $"{targetLow:N0}–{targetHigh:N0}";
        if (wordCount < targetLow)
            return $"{count} — ≈{Round(targetLow - wordCount):N0} words short of the {range} genre norm";
        if (wordCount > targetHigh)
            return $"{count} — ≈{Round(wordCount - targetHigh):N0} words over the {range} genre norm";
        return $"{count} — within the {range} genre norm";
    }

    // The gap is an estimate, so round to the nearest thousand rather than implying false precision.
    private static int Round(int gap) => (int)System.Math.Round(gap / 1000.0) * 1000;
}
