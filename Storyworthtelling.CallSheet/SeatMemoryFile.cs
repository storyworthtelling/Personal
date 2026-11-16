using System.Text.Json;

namespace Storyworthtelling.CallSheet;

/// <summary>
/// Where a head keeps what each seat was last given, so a line survives a restart (audit W10.3). One small JSON
/// file per app, the newest <see cref="Entries"/> kept. A seat that speaks about someone in particular (a child,
/// a title, a book) passes a <c>subject</c>, so two of them never share one line. A missing or damaged file
/// remembers nothing, which only means a line is asked for again.
/// </summary>
public sealed class SeatMemoryFile(string path)
{
    public const int Entries = 64;

    private static readonly object Gate = new();

    public SeatMemory? Get(VoiceLine line, string subject = "")
    {
        lock (Gate)
        {
            return Read().TryGetValue(Key(line, subject), out var memory) ? memory : null;
        }
    }

    public void Put(VoiceLine line, string subject, SeatMemory memory)
    {
        lock (Gate)
        {
            var all = Read();
            var key = Key(line, subject);
            all.Remove(key);
            var kept = all.TakeLast(Entries - 1).Append(new KeyValuePair<string, SeatMemory>(key, memory))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(kept));
            File.Move(temp, path, overwrite: true);
        }
    }

    private static string Key(VoiceLine line, string subject) => $"{line.App}/{line.Seat}/{subject}";

    // In insertion order, so the oldest go first when the file is full.
    private Dictionary<string, SeatMemory> Read()
    {
        if (!File.Exists(path))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, SeatMemory>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return [];
        }
    }
}
