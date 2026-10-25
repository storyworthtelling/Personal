using System.Globalization;
using System.Text.Json;

namespace Constitution.Domain.Meals;

/// <summary>What reading the estimates gave: what was kept, and whether the file could not be read (in which case none are shown, and never a zero).</summary>
public sealed record AteStoreRead(IReadOnlyList<AteEstimate> Items, bool Unreadable);

/// <summary>
/// Where estimates are kept: this install only, in a small local file the backup does not include. It is not a household record kind
/// and it is not in Drive. One estimate per day and meal; a later one replaces the earlier and hands it back so an undo can restore it.
/// </summary>
public interface IAteEstimates
{
    AteStoreRead Read();

    /// <summary>Keeps an estimate, returning the one for the same day and meal that it replaced, if any.</summary>
    AteEstimate? Record(AteEstimate estimate);

    /// <summary>Takes back an estimate and puts back the one it replaced.</summary>
    void Withdraw(AteEstimate recorded, AteEstimate? previous);
}

/// <summary>The estimates file's shape. Wire names are written out, never derived, so a renamed property cannot orphan a file.</summary>
public static class AteCodec
{
    public const int Version = 1;

    /// <summary>Days an estimate is kept for. Older ones are let go when a new one is recorded.</summary>
    public const int KeptDays = 60;

    private const string DayFormat = "yyyy-MM-dd";

    public static string Write(IEnumerable<AteEstimate> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartArray("items");
            foreach (var item in items)
            {
                writer.WriteStartObject();
                writer.WriteString("day", item.Day.ToString(DayFormat, CultureInfo.InvariantCulture));
                writer.WriteString("meal", AteMeals.Word(item.Meal));
                writer.WriteString("what", item.Reading.What);
                writer.WriteNumber("kcalLow", item.Reading.KcalLow);
                writer.WriteNumber("kcalHigh", item.Reading.KcalHigh);
                if (item.Reading.ProteinLow is { } low && item.Reading.ProteinHigh is { } high)
                {
                    writer.WriteNumber("proteinLow", low);
                    writer.WriteNumber("proteinHigh", high);
                }

                writer.WriteString("confidence", item.Reading.Confidence.ToString().ToLowerInvariant());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>No text is an empty record; text that is not a whole, valid file is unreadable, and nothing of it is trusted.</summary>
    public static AteStoreRead Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new AteStoreRead([], false);
        }

        try
        {
            using var parsed = JsonDocument.Parse(text);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != Version
                || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return Unreadable();
            }

            var read = new List<AteEstimate>();
            foreach (var item in items.EnumerateArray())
            {
                if (ReadOne(item) is not { } estimate)
                {
                    return Unreadable();
                }

                read.Add(estimate);
            }

            return new AteStoreRead(read, false);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            // A garbled file is reported as unreadable by the return value; the caller keeps it aside and says so.
            return Unreadable();
        }
    }

    /// <summary>Keeps the estimates from the last <see cref="KeptDays"/> days before <paramref name="today"/>.</summary>
    public static IReadOnlyList<AteEstimate> Trim(IEnumerable<AteEstimate> items, DateOnly today) =>
        items.Where(item => item.Day.DayNumber >= today.DayNumber - KeptDays).ToList();

    private static AteStoreRead Unreadable() => new([], true);

    private static AteEstimate? ReadOne(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !DateOnly.TryParseExact(Text(item, "day"), DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            || MealOf(Text(item, "meal")) is not { } meal
            || !item.TryGetProperty("kcalLow", out var low) || !item.TryGetProperty("kcalHigh", out var high)
            || !low.TryGetInt64(out var kcalLow) || !high.TryGetInt64(out var kcalHigh))
        {
            return null;
        }

        long? proteinLow = item.TryGetProperty("proteinLow", out var pl) && pl.TryGetInt64(out var plv) ? plv : null;
        long? proteinHigh = item.TryGetProperty("proteinHigh", out var ph) && ph.TryGetInt64(out var phv) ? phv : null;
        var confidence = Text(item, "confidence") switch
        {
            "good" => AteConfidence.Good,
            "fair" => AteConfidence.Fair,
            "low" => AteConfidence.Low,
            _ => (AteConfidence?)null,
        };

        return confidence is { } sure && AteReading.TryCreate(Text(item, "what"), kcalLow, kcalHigh, proteinLow, proteinHigh, sure, out var reading)
            ? new AteEstimate(day, meal, reading!)
            : null;
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static AteMeal? MealOf(string? word) => word switch
    {
        "breakfast" => AteMeal.Breakfast,
        "lunch" => AteMeal.Lunch,
        "dinner" => AteMeal.Dinner,
        _ => null,
    };
}
