using System.Globalization;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Body;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// What has gone to the tablet, in one file in the app's private storage, read and written whole on every call. It holds the
/// title, whose and when, the slot, and a fingerprint of what the page said: never a dose, a load or a line of the page.
///
/// <para>An entry is let go when a newer one is recorded and it is more than <see cref="KeepDays"/> days older than that
/// one: a page that old has been read back or given up on. A file that cannot be read is set aside
/// (<see cref="UnreadableCopies.SetAside"/>) and the store carries on empty, which costs a page possibly sent again, not a page lost.</para>
/// </summary>
public sealed class FileDeliveredWorkouts : IDeliveredWorkouts
{
    public const int KeepDays = 14;

    private const int Version = 1;
    private const string DayFormat = "yyyy-MM-dd";
    private static readonly object FileLock = new();

    private readonly string path;

    public FileDeliveredWorkouts(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public DeliveredWorkout? Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (FileLock)
        {
            return Load().FirstOrDefault(item => item.Name == name);
        }
    }

    public void Record(DeliveredWorkout delivered)
    {
        ArgumentNullException.ThrowIfNull(delivered);
        lock (FileLock)
        {
            var oldest = delivered.Day.AddDays(-KeepDays);
            var kept = Load().Where(item => item.Name != delivered.Name && item.Day >= oldest).ToList();
            kept.Add(delivered);
            Save(kept);
        }
    }

    public void Remove(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (FileLock)
        {
            var all = Load();
            if (all.Any(item => item.Name == name))
            {
                Save(all.Where(item => item.Name != name).ToList());
            }
        }
    }

    private List<DeliveredWorkout> Load()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return Decode(File.ReadAllText(path));
        }
        catch (FormatException)
        {
            UnreadableCopies.SetAside(path);
            return [];
        }
    }

    private void Save(List<DeliveredWorkout> all) => AtomicFile.WriteAllText(path, Encode(all));

    private static string Encode(IEnumerable<DeliveredWorkout> all)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartArray("delivered");
            foreach (var item in all.OrderBy(entry => entry.Day).ThenBy(entry => entry.Name, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("name", item.Name);
                writer.WriteString("person", item.Person);
                writer.WriteString("day", item.Day.ToString(DayFormat, CultureInfo.InvariantCulture));
                writer.WriteString("kind", WorkoutWireNames.Of(item.Kind));
                writer.WriteString("slotStart", item.SlotStart.ToString("O", CultureInfo.InvariantCulture));
                writer.WriteString("fingerprint", item.Fingerprint);
                writer.WriteBoolean("doctorNote", item.CarriesDoctorNote);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static List<DeliveredWorkout> Decode(string json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            if (root.GetProperty("version").GetInt32() != Version)
            {
                throw new FormatException("This record is of a version this app does not read.");
            }

            return root.GetProperty("delivered").EnumerateArray().Select(Read).ToList();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            throw new FormatException("The record could not be read as the workout pages delivered.", e);
        }
    }

    private static DeliveredWorkout Read(JsonElement item)
    {
        var kind = WorkoutWireNames.SessionKindOf(Text(item, "kind"))
            ?? throw new FormatException("The record names a kind of session this app does not know.");

        return new DeliveredWorkout(
            Text(item, "name"),
            Text(item, "person"),
            DateOnly.ParseExact(Text(item, "day"), DayFormat, CultureInfo.InvariantCulture),
            kind,
            DateTimeOffset.Parse(Text(item, "slotStart"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            Text(item, "fingerprint"),
            item.GetProperty("doctorNote").GetBoolean());
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            ? text
            : throw new FormatException($"The record has no text called '{name}' where one belongs.");
}
