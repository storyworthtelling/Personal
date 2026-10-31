using System.Globalization;
using System.Text.Json;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>What the meal run asks for: the notes the owner wrote to the planner, for the week it is planning.</summary>
public interface IPlannerNotesSource
{
    IReadOnlyList<string> For(DateOnly weekStart);
}

/// <summary>The planner notes read off the Saturday digest, kept until the planner has used them.</summary>
public interface IPlannerNotes : IPlannerNotesSource
{
    /// <summary>Whether this adult's notes for that Saturday's digest were already read (even if the lines were blank).</summary>
    bool Has(string person, DateOnly saturday);

    void Save(string person, DateOnly saturday, IReadOnlyList<string> notes);
}

/// <summary>
/// The notes in one small private file. A note written on a Saturday informs a plan made in the three weeks after it, in the order written and adult by adult; older notes are
/// forgotten, so a note cannot steer a plan it was never about. A damaged file reads as no notes and is rewritten whole by the next save.
/// </summary>
public sealed class FilePlannerNotes(string path) : IPlannerNotes
{
    public const int WeeksReach = 3;

    private static readonly object FileLock = new();

    private sealed record Entry(string Person, string Saturday, List<string> Notes);

    public bool Has(string person, DateOnly saturday)
    {
        lock (FileLock)
        {
            return Read().Any(entry => Same(entry, person, saturday));
        }
    }

    public IReadOnlyList<string> For(DateOnly weekStart)
    {
        lock (FileLock)
        {
            return Read()
                .Where(entry => DateOnly.TryParseExact(entry.Saturday, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                    && day < weekStart && weekStart.DayNumber - day.DayNumber <= 7 * WeeksReach + 1)
                .OrderBy(entry => entry.Saturday, StringComparer.Ordinal)
                .ThenBy(entry => entry.Person, StringComparer.OrdinalIgnoreCase)
                .SelectMany(entry => entry.Notes)
                .ToList();
        }
    }

    public void Save(string person, DateOnly saturday, IReadOnlyList<string> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        lock (FileLock)
        {
            var kept = Read().Where(entry => !Same(entry, person, saturday)).ToList();
            kept.Add(new Entry(person.Trim(), saturday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), notes.ToList()));
            kept = kept.OrderByDescending(entry => entry.Saturday, StringComparer.Ordinal).Take(16).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(kept));
            File.Move(temp, path, true);
        }
    }

    private static bool Same(Entry entry, string person, DateOnly saturday) =>
        string.Equals(entry.Person, person.Trim(), StringComparison.OrdinalIgnoreCase)
        && entry.Saturday == saturday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private List<Entry> Read()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) is { } entries
                ? entries.Where(entry => entry is not null && entry.Person is not null && entry.Saturday is not null && entry.Notes is not null).ToList()
                : [];
        }
        catch (JsonException)
        {
            // A damaged file means no notes, which only costs the planner a hint; the next save rewrites it whole.
            return [];
        }
    }
}
