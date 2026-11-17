namespace Storyworthtelling.Voice;

/// <summary>
/// What of a manuscript the voice is learned from (audit W10.6): a few windows spread from its first page to its
/// last, each cut on a word's edge, rather than its opening. The first twelve thousand characters of a book are its
/// title page, dedication and first chapter, and the voice of a book's first pages is not the voice of the book.
/// </summary>
public static class VoiceSample
{
    /// <summary>The default length: what a single refine has always been sent.</summary>
    public const int Budget = 12000;

    /// <summary>How many windows a long text is read through.</summary>
    public const int Windows = 4;

    /// <summary>What stands between two windows, so the model reads them as separate passages.</summary>
    public const string Separator = "\n\n[…]\n\n";

    /// <summary>The whole text when it fits <paramref name="budget"/>, else <see cref="Windows"/> evenly spaced runs of it.</summary>
    public static string Of(string text, int budget = Budget)
    {
        var whole = text.Trim();
        if (whole.Length <= budget)
            return whole;

        var size = budget / Windows;
        var step = (whole.Length - size) / (double)(Windows - 1);
        var windows = new List<string>(Windows);
        for (var index = 0; index < Windows; index++)
        {
            var start = (int)Math.Round(index * step);
            var end = Math.Min(start + size, whole.Length);

            // Forward to the start of a word, back to the end of one.
            if (start > 0)
            {
                while (start < end && !char.IsWhiteSpace(whole[start - 1]))
                    start++;
            }

            if (end < whole.Length)
            {
                while (end > start && !char.IsWhiteSpace(whole[end]))
                    end--;
            }

            var window = whole[start..end].Trim();
            if (window.Length > 0)
                windows.Add(window);
        }

        return string.Join(Separator, windows);
    }
}
