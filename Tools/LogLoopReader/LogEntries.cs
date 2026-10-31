using System.Text.RegularExpressions;

namespace LogLoopReader;

/// <summary>One entry as DiagnosticLogFormatter writes it: <c>[stamp] LEVEL Source — Message</c>, then detail lines.</summary>
public sealed record LogEntry(string Level, string Source, string Message, string Detail);

/// <summary>
/// Splits a log segment into entries. DiagnosticLog separates entries with a blank line and starts each
/// with a bracketed stamp; the outbox's <c>DROPPED</c> line and anything else unshaped become their own
/// entries rather than being lost.
/// </summary>
public static partial class LogEntries
{
    public static IReadOnlyList<LogEntry> Parse(string text)
    {
        var entries = new List<LogEntry>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        List<string>? current = null;

        void Flush()
        {
            if (current is { Count: > 0 })
            {
                entries.Add(Make(current));
            }
            current = null;
        }

        foreach (var line in lines)
        {
            if (Header().IsMatch(line) || line.StartsWith("DROPPED ", StringComparison.Ordinal))
            {
                Flush();
                current = [line];
            }
            else if (line.Length == 0)
            {
                // A blank line ends an entry, but only a header starts the next; a blank inside a detail
                // (rare) simply joins it.
                current?.Add(line);
            }
            else
            {
                (current ??= []).Add(line);
            }
        }
        Flush();
        return entries;
    }

    private static LogEntry Make(List<string> lines)
    {
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }
        var first = lines[0];
        var detail = string.Join('\n', lines.Skip(1));
        var match = Header().Match(first);
        if (!match.Success)
        {
            var word = first.Split(' ', 2)[0];
            return new LogEntry(word == "DROPPED" ? "DROPPED" : "TEXT", string.Empty, first, detail);
        }
        return new LogEntry(match.Groups["level"].Value, match.Groups["source"].Value, match.Groups["message"].Value, detail);
    }

    [GeneratedRegex(@"^\[[^\]]+\] (?<level>[A-Z]+) (?<source>\S+) — (?<message>.*)$")]
    private static partial Regex Header();
}
