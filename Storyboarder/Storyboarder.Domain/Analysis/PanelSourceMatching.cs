namespace Storyboarder.Domain.Analysis;

// The single definition of "which stretch of manuscript a panel was drawn from", shared by the draft
// diff and the incremental re-render so the two never disagree about what counts as a match.
public static class PanelSourceMatching
{
    // The exact source text a panel sits on, or null when the panel can't be matched across drafts: a
    // zero-width span (a panel from before source anchoring existed) or offsets that fall outside the
    // text. An unmatchable panel always re-renders rather than risk carrying forward the wrong art.
    public static string? SourceText(string fullText, int startOffset, int endOffset)
    {
        if (startOffset < 0 || endOffset <= startOffset || endOffset > fullText.Length)
            return null;
        return fullText.Substring(startOffset, endOffset - startOffset);
    }
}
