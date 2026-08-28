using System.Globalization;
using System.Text.RegularExpressions;

namespace Codex.Domain.Music;

/// <summary>One line, and when it is sung. <see cref="At"/> is zero for untimed words.</summary>
public sealed record LyricLine(TimeSpan At, string Text);

/// <summary>
/// The words to a track, timed if anyone has timed them.
/// </summary>
/// <param name="Timed">
/// Whether the times mean anything. Most lyrics on the open web are not timed, and
/// they are still worth reading — but a panel that followed along using zeroes would
/// hold the first line of the song lit for its whole length, so the distinction has
/// to travel with the words.
/// </param>
public sealed record Lyrics(IReadOnlyList<LyricLine> Lines, bool Timed)
{
    public static Lyrics None { get; } = new([], false);

    public bool IsEmpty => Lines.Count == 0;

    /// <summary>
    /// Which line is being sung at this position, or -1 for none — before the first
    /// word, or when the words are not timed at all.
    ///
    /// A line holds until the next one starts rather than for a fixed span, because
    /// nothing in an LRC file says how long a line lasts and guessing would make the
    /// highlight flicker off during every held note.
    /// </summary>
    public int LineAt(TimeSpan position)
    {
        if (!Timed)
        {
            return -1;
        }

        var current = -1;
        for (var i = 0; i < Lines.Count && Lines[i].At <= position; i++)
        {
            current = i;
        }

        return current;
    }
}

/// <summary>
/// Reads the LRC format — the one interchange format for timed lyrics, and the shape
/// every source hands them over in.
///
/// Pure, and tested rather than trusted, because the format is loose in ways that
/// matter: one line can carry several timestamps (how a repeated chorus is written
/// without repeating the words), files open with metadata tags that are not words
/// anyone sings, and an empty timed line is a real thing — a rest — that has to
/// survive so the highlight can clear through an instrumental instead of holding the
/// last line for a minute.
/// </summary>
public static partial class LrcParser
{
    public static Lyrics Parse(string? synced, string? plain)
    {
        var timed = ParseSynced(synced);
        if (timed.Count > 0)
        {
            return new Lyrics(timed, Timed: true);
        }

        if (string.IsNullOrWhiteSpace(plain))
        {
            return Lyrics.None;
        }

        return new Lyrics(
            [.. plain.Replace("\r", string.Empty).Split('\n').Select(line => new LyricLine(TimeSpan.Zero, line.Trim()))],
            Timed: false);
    }

    private static List<LyricLine> ParseSynced(string? synced)
    {
        var lines = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(synced))
        {
            return lines;
        }

        foreach (var raw in synced.Replace("\r", string.Empty).Split('\n'))
        {
            var stamps = Timestamp().Matches(raw);
            if (stamps.Count == 0)
            {
                continue; // metadata, or a line nobody timed
            }

            var text = raw[(stamps[^1].Index + stamps[^1].Length)..].Trim();
            foreach (Match stamp in stamps)
            {
                if (At(stamp) is { } at)
                {
                    lines.Add(new LyricLine(at, text));
                }
            }
        }

        // Sorted, because a file written by hand is not necessarily in order and the
        // current-line search walks forward.
        lines.Sort((a, b) => a.At.CompareTo(b.At));
        return lines;
    }

    private static TimeSpan? At(Match stamp)
    {
        if (!int.TryParse(stamp.Groups[1].Value, out var minutes)
            || !double.TryParse(stamp.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        return TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
    }

    // [mm:ss] or [mm:ss.xx] — hundredths or thousandths, both in the wild.
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2}(?:[.:]\d{1,3})?)\]")]
    private static partial Regex Timestamp();
}
