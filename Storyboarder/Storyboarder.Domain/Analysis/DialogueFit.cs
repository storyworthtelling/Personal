namespace Storyboarder.Domain.Analysis;

// Trims a panel's dialogue so it fits a speech balloon without crowding the art. Comics abridge speech
// to what a balloon can hold — here readability wins over verbatim, unlike the artwork, which stays
// faithful to the text. Trims back to a word boundary and marks the cut with an ellipsis; dialogue that
// already fits is returned unchanged.
public static class DialogueFit
{
    // A couple of short lines — enough for a balloon that doesn't dominate the panel. Long speeches get
    // cut here so the composer lays out complete (abridged) text instead of clipping mid-sentence.
    public const int DefaultMaxChars = 160;

    public static string? Trim(string? dialogue, int maxChars = DefaultMaxChars)
    {
        if (string.IsNullOrWhiteSpace(dialogue))
            return dialogue;

        var text = dialogue.Trim();
        if (text.Length <= maxChars)
            return text;

        // Leave room for the ellipsis, then cut back to the last whole word within budget.
        var budget = Math.Max(1, maxChars - 1);
        var slice = text[..budget];
        var lastSpace = slice.LastIndexOf(' ');
        if (lastSpace > 0)
            slice = slice[..lastSpace];
        return slice.TrimEnd(' ', ',', ';', ':', '-', '—', '.') + "…";
    }
}
