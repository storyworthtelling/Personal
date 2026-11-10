using System.Collections.Concurrent;

namespace Storyworthtelling.Diagnostics;

/// <summary>
/// The same failure, repeated: logged in full the first time in a window, counted after that, and the count said when
/// the next window opens (audit 08 §33). A missing key fails every title of every refresh the same way; a full line and
/// stack each time rolled the log and filled the outbox.
/// </summary>
public sealed class RepeatedFailures(TimeSpan window)
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset Start, int Count)> windows = new(StringComparer.Ordinal);
    private readonly object gate = new();

    /// <summary>Whether to log this one in full, and the previous window's count to say with it, if any.</summary>
    public (bool LogInFull, string? Summary) Note(string kind, DateTimeOffset now)
    {
        lock (gate)
        {
            if (windows.TryGetValue(kind, out var open) && now - open.Start < window)
            {
                windows[kind] = (open.Start, open.Count + 1);
                return (false, null);
            }

            windows[kind] = (now, 0);
            var summary = open.Count > 0 ? $"{open.Count} more of the same in the {window.TotalMinutes:0} minutes before" : null;
            return (true, summary);
        }
    }
}
