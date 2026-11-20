using System.Text.Json;
using Storyworthtelling.Household;

namespace Storyworthtelling.People;

/// <summary>
/// This install's own small facts for the household file, kept beside its copy: the id it writes records under and the last stamp its
/// clock gave, so a restart never stamps older than it did. A file that cannot be read is set aside and a new id begun: the clock is moved
/// past everything the household holds before every write, so nothing that matters is lost.
/// </summary>
public sealed class PeopleInstallStore
{
    public const string FileName = "people-install.json";

    private readonly object gate = new();

    public PeopleInstallStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
        FilePath = Path.Combine(directory, FileName);
    }

    public string Directory { get; }

    public string FilePath { get; }

    public (string InstallId, HlcTimestamp LastStamp) Load()
    {
        lock (gate)
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath));
                    if (stored is { InstallId.Length: > 0 })
                    {
                        return (stored.InstallId, new HlcTimestamp(stored.Wall, stored.Counter));
                    }
                }
                catch (JsonException)
                {
                }

                File.Move(FilePath, FilePath + ".unreadable-" + DateTime.UtcNow.Ticks, overwrite: true);
            }

            var fresh = new Stored("install-" + Guid.NewGuid().ToString("N"), 0, 0);
            Write(fresh);
            return (fresh.InstallId, HlcTimestamp.Zero);
        }
    }

    public void SaveClock(HlcTimestamp stamp)
    {
        lock (gate)
        {
            var (id, _) = Load();
            Write(new Stored(id, stamp.WallMillis, stamp.Counter));
        }
    }

    private void Write(Stored stored)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(stored));
        File.Move(temporary, FilePath, overwrite: true);
    }

    private sealed record Stored(string InstallId, long Wall, int Counter);
}
