using System.Globalization;
using System.Text.Json;
using Android.Content;
using Constitution.Domain.Voice;
using Constitution.Infrastructure.Voice;

namespace Constitution.BodyTab;

/// <summary>
/// Keeps the voiced lines a seat has already composed, so a seat is composed at most once for the same
/// facts and day (C-9.2.3) and the same sentence is on the page when the owner comes back to it. A voiced
/// line is sentence about the owner's health, so it lives in the app's no-backup folder, beside nothing the
/// backup carries, and goes at the next day's turn (<see cref="DropBefore"/>). Only voiced lines are ever
/// saved: <see cref="SeatCache"/> never hands over the plain fallback.
/// </summary>
internal sealed class BodySeatLineStore : ISeatLineStore
{
    private const string FileName = "seat-lines.json";
    private const string DateFormat = "yyyy-MM-dd";

    // One lock for the process: two stores on one file must not interleave a read-modify-write.
    private static readonly object FileLock = new();

    private readonly string path;

    public BodySeatLineStore(Context context)
    {
        var directory = (context.ApplicationContext ?? context).NoBackupFilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no no-backup folder to keep a voiced line in.");
        path = Path.Combine(directory, FileName);
    }

    public SeatLine? Find(SeatLineKey key)
    {
        lock (FileLock)
        {
            return Load().TryGetValue(key.ToString(), out var stored) && Enum.TryParse<VoiceSeat>(stored.Seat, out var seat)
                ? new SeatLine(seat, stored.Text, IsVoiced: true)
                : null;
        }
    }

    public void Save(SeatLineKey key, SeatLine line)
    {
        lock (FileLock)
        {
            var lines = Load();
            lines[key.ToString()] = new Stored(line.Seat.ToString(), line.Text);
            Write(lines);
        }
    }

    public void DropBefore(DateOnly day)
    {
        lock (FileLock)
        {
            var lines = Load();
            var stale = lines.Keys.Where(key => DayOf(key) is { } keyDay && keyDay < day).ToList();
            if (stale.Count == 0)
            {
                return;
            }

            foreach (var key in stale)
            {
                lines.Remove(key);
            }

            Write(lines);
        }
    }

    private sealed record Stored(string Seat, string Text);

    // A key reads "seat|hash|yyyy-MM-dd" (SeatLineKey.ToString): the day is its last part.
    private static DateOnly? DayOf(string key) =>
        DateOnly.TryParseExact(key[(key.LastIndexOf('|') + 1)..], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

    private Dictionary<string, Stored> Load()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, Stored>>(File.ReadAllText(path)) ?? [];
        }
        catch (JsonException)
        {
            // A damaged cache is only a cache: the seat is composed again, which is what it would do on a new day.
            File.Delete(path);
            return [];
        }
    }

    private void Write(Dictionary<string, Stored> lines)
    {
        var staging = path + ".tmp";
        File.WriteAllText(staging, JsonSerializer.Serialize(lines));
        File.Move(staging, path, overwrite: true);
    }
}
