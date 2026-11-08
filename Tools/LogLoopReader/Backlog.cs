using System.Globalization;

namespace LogLoopReader;

/// <summary>
/// The files still in the folder that this week did not finish, with how long they have waited. A file that keeps
/// failing to decrypt, or never gets triaged, otherwise sits unseen; the plan's four-week rule needs an age to apply to.
/// Names and counts only, never a line.
/// </summary>
public static class Backlog
{
    public const int OverdueDays = 28;

    public static IReadOnlyList<string> Lines(IEnumerable<Manifest.Entry> entries, Func<string, DateTimeOffset?> lastWritten, DateTimeOffset now) =>
        entries
            .Where(e => e.State != "done")
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .Select(e =>
            {
                var why = $"{e.State}{(e.Status is { Length: > 0 } and not "Decrypted" ? ", " + e.Status : string.Empty)}";
                if (lastWritten(e.Name) is not { } written)
                {
                    return $"{e.Name}: age unknown ({why})";
                }
                var days = (int)(now - written).TotalDays;
                return $"{e.Name}: {days.ToString(CultureInfo.InvariantCulture)} days ({why}){(days >= OverdueDays ? " OVERDUE" : string.Empty)}";
            })
            .ToList();
}
