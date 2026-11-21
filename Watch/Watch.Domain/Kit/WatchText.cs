namespace Watch.Domain.Kit;

/// <summary>What <see cref="WatchText"/> made of a line.</summary>
public enum WatchTextVerdict
{
    Fits,
    Empty,
    MoreThanOneSentence,
    Exclaims,
    TooLong,
}

/// <summary>The verdict, and the lines to draw when it fits (none otherwise).</summary>
public sealed record WatchTextLayout(WatchTextVerdict Verdict, IReadOnlyList<string> Lines);

/// <summary>
/// The watch's words (WD §5, §6; A37; P14 W-1.2.1): one short sentence at most, no exclamation mark,
/// wrapped once at a word and never shrunk. There is no size here to shrink: a line that does not fit
/// two lines at the token size is refused, and the screen's words are what change.
/// </summary>
public static class WatchText
{
    public const int MaxLines = 2;

    private static readonly char[] Ends = ['.', '?', '!', '…'];

    /// <summary>The rules that need no measuring: something to say, one sentence, no exclamation.</summary>
    public static WatchTextVerdict Check(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return WatchTextVerdict.Empty;
        }
        if (text.Contains('!', StringComparison.Ordinal))
        {
            return WatchTextVerdict.Exclaims;
        }
        var trimmed = text.Trim();
        for (var index = 0; index < trimmed.Length - 1; index++)
        {
            if (Array.IndexOf(Ends, trimmed[index]) >= 0 && char.IsWhiteSpace(trimmed[index + 1]))
            {
                return WatchTextVerdict.MoreThanOneSentence;
            }
        }
        return WatchTextVerdict.Fits;
    }

    /// <summary>
    /// The sentence laid out in <paramref name="width"/>, measured by <paramref name="measure"/> at the
    /// one size it is drawn at: greedy at word breaks, at most <see cref="MaxLines"/> lines.
    /// </summary>
    public static WatchTextLayout Lay(string text, double width, Func<string, double> measure)
    {
        var verdict = Check(text);
        if (verdict != WatchTextVerdict.Fits)
        {
            return new WatchTextLayout(verdict, []);
        }

        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (measure(word) > width)
            {
                return TooLong();
            }
            var next = line.Length == 0 ? word : line + " " + word;
            if (measure(next) <= width)
            {
                line = next;
                continue;
            }
            lines.Add(line);
            if (lines.Count == MaxLines)
            {
                return TooLong();
            }
            line = word;
        }
        lines.Add(line);
        return new WatchTextLayout(WatchTextVerdict.Fits, lines);
    }

    private static WatchTextLayout TooLong() => new(WatchTextVerdict.TooLong, []);
}
