using System.Text.Json;

namespace Constitution.Infrastructure.Documents;

/// <summary>
/// The digest's stamps in one small private file: "person|Saturday" lines, newest eight weeks kept for each adult. A file that will not parse reads as no stamps,
/// the safe way round: the worst case is one digest sent again, never one skipped.
/// </summary>
public sealed class FileDigestStamps(string path) : IDigestStamps
{
    public const int WeeksKept = 8;

    private static readonly object FileLock = new();

    public bool Has(string person, DateOnly saturday)
    {
        lock (FileLock)
        {
            return Read().Contains(Line(person, saturday), StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Mark(string person, DateOnly saturday)
    {
        lock (FileLock)
        {
            var lines = Read();
            lines.Add(Line(person, saturday));
            var kept = lines
                .Select(line => line.Split('|'))
                .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
                .SelectMany(group => group.OrderByDescending(parts => parts[1], StringComparer.Ordinal).Take(WeeksKept))
                .Select(parts => parts[0] + "|" + parts[1])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(kept));
            File.Move(temp, path, true);
        }
    }

    private static string Line(string person, DateOnly saturday) =>
        person.Trim() + "|" + saturday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private List<string> Read()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) is { } lines
                ? lines.Where(line => line.Split('|').Length == 2).ToList()
                : [];
        }
        catch (JsonException)
        {
            // A damaged stamp file means "nothing stamped", which can only send a digest again; the next Mark rewrites it whole.
            return [];
        }
    }
}
