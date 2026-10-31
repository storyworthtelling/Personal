using System.Globalization;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Delivery;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Body;
using Constitution.Infrastructure.Workouts;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// What the meal run has put on the tablet, in one file in the app's private storage, read and written whole on every call. It holds the title,
/// whose and when, the slot, and a fingerprint of what the page said: never a line of the page.
///
/// <para>An entry is let go when a newer one is recorded and it is more than <see cref="KeepDays"/> days older than that one: a page that old has been
/// read back or given up on. A file that cannot be read is set aside (<see cref="UnreadableCopies.SetAside"/>) and the store carries on empty, which
/// costs a page possibly looked up on the tablet again (a first send never makes a second document), not a page lost.</para>
/// </summary>
public sealed class FileDeliveredMealDocuments : IDeliveredMealDocuments
{
    public const int KeepDays = 14;

    private const int Version = 1;
    private const string DayFormat = "yyyy-MM-dd";
    private const string MealsWord = "meals";
    private const string RecipeWord = "recipe";
    private static readonly object FileLock = new();

    private readonly string path;

    public FileDeliveredMealDocuments(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public DeliveredMealDocument? Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (FileLock)
        {
            return Load().FirstOrDefault(item => item.Name == name);
        }
    }

    public void Record(DeliveredMealDocument delivered)
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

    private List<DeliveredMealDocument> Load()
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

    private void Save(List<DeliveredMealDocument> all) => AtomicFile.WriteAllText(path, Encode(all));

    private static string Encode(IEnumerable<DeliveredMealDocument> all)
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
                writer.WriteString("kind", WordOf(item.Kind));
                writer.WriteString("person", item.Person);
                writer.WriteString("day", item.Day.ToString(DayFormat, CultureInfo.InvariantCulture));
                writer.WriteString("slotStart", item.SlotStart.ToString("O", CultureInfo.InvariantCulture));
                writer.WriteString("fingerprint", item.Fingerprint);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static List<DeliveredMealDocument> Decode(string json)
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
            throw new FormatException("The record could not be read as the meal documents delivered.", e);
        }
    }

    private static DeliveredMealDocument Read(JsonElement item) =>
        new(
            Text(item, "name"),
            KindOf(Text(item, "kind")),
            Text(item, "person"),
            DateOnly.ParseExact(Text(item, "day"), DayFormat, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(Text(item, "slotStart"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            Text(item, "fingerprint"));

    // The stored words are literals, never enum names: renaming a member must not rename what is already on the phone.
    private static string WordOf(DeliveryKind kind) => kind switch
    {
        DeliveryKind.Meals => MealsWord,
        DeliveryKind.Recipe => RecipeWord,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only the sheet and the recipe are recorded here."),
    };

    private static DeliveryKind KindOf(string word) => word switch
    {
        MealsWord => DeliveryKind.Meals,
        RecipeWord => DeliveryKind.Recipe,
        _ => throw new FormatException("The record names a kind of document this app does not know."),
    };

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            ? text
            : throw new FormatException($"The record has no text called '{name}' where one belongs.");
}
