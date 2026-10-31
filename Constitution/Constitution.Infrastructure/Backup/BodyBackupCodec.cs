using System.Globalization;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Body;

namespace Constitution.Infrastructure.Backup;

/// <summary>
/// Thrown when a Body backup's plaintext is not a Body backup. The message says what is wrong with the
/// shape, never what was there: it ends up in front of the owner and in a diagnostics trail, and a
/// reading must not travel with it. The parser's own exception, which may say where it stopped, is kept
/// as the inner exception for debugging.
/// </summary>
public sealed class BodyBackupFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// A backup read back.
/// </summary>
/// <param name="History">Every reading and the install day this build could make sense of.</param>
/// <param name="FormatVersion">The format version the file declared.</param>
/// <param name="HasUnreadParts">
/// True when the file holds anything this build skipped: a format newer than it knows, or a signal id it
/// has no signal for. Reading such a file is fine; writing over it is not, because the write would drop
/// whatever was skipped.
/// </param>
public sealed record DecodedBodyBackup(BodyHistory History, int FormatVersion, bool HasUnreadParts);

/// <summary>
/// The plaintext inside the Body backup's envelope: JSON with a format version, the install day, and the
/// readings grouped by each signal's frozen id (<see cref="BodySignals.Id"/>). The names are literals on
/// purpose; they are what a restore on a new phone has to find.
///
/// <para>Written the same way every time (signals in their fixed order, days oldest first, no
/// whitespace), so an unchanged history writes an unchanged file. Read leniently where a newer build
/// might have added something (unknown signals and unknown properties are skipped, and reported) and
/// strictly where the shape is simply wrong.</para>
/// </summary>
public static class BodyBackupCodec
{
    /// <summary>The format this build writes, and the newest it fully understands.</summary>
    public const int CurrentVersion = 1;

    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>Writes a history as the backup's plaintext.</summary>
    public static string Write(BodyHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", CurrentVersion);
            if (history.InstallDay is { } installDay)
            {
                writer.WriteString("installDay", Day(installDay));
            }
            else
            {
                writer.WriteNull("installDay");
            }

            writer.WriteStartObject("readings");
            foreach (var group in history.Readings.GroupBy(reading => reading.Signal))
            {
                writer.WriteStartArray(BodySignals.Id(group.Key));
                foreach (var reading in group)
                {
                    writer.WriteStartObject();
                    writer.WriteString("day", Day(reading.Day));
                    writer.WriteNumber("value", reading.Value);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads a backup's plaintext, or throws <see cref="BodyBackupFormatException"/>.</summary>
    public static DecodedBodyBackup Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
        }
        catch (JsonException ex)
        {
            throw new BodyBackupFormatException("The Body backup is not valid JSON.", ex);
        }

        using (document)
        {
            return Decode(document.RootElement);
        }
    }

    private static DecodedBodyBackup Decode(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Wrong("the backup is not a JSON object");
        }

        if (!root.TryGetProperty("version", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.Number
            || !versionElement.TryGetInt32(out var version)
            || version < 1)
        {
            throw Wrong("the backup has no usable format version");
        }

        DateOnly? installDay = null;
        if (root.TryGetProperty("installDay", out var dayElement) && dayElement.ValueKind != JsonValueKind.Null)
        {
            installDay = ReadDay(dayElement, "the install day");
        }

        if (!root.TryGetProperty("readings", out var groups) || groups.ValueKind != JsonValueKind.Object)
        {
            throw Wrong("the backup has no readings object");
        }

        var readings = new List<BodyReading>();
        var unread = version > CurrentVersion;
        foreach (var group in groups.EnumerateObject())
        {
            if (!BodySignals.TryFromId(group.Name, out var signal))
            {
                unread = true;
                continue;
            }

            if (group.Value.ValueKind != JsonValueKind.Array)
            {
                throw Wrong("a signal's readings are not a list");
            }

            foreach (var entry in group.Value.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    throw Wrong("a reading is not an object");
                }

                if (!entry.TryGetProperty("day", out var entryDay))
                {
                    throw Wrong("a reading has no day");
                }

                if (!entry.TryGetProperty("value", out var entryValue)
                    || entryValue.ValueKind != JsonValueKind.Number
                    || !entryValue.TryGetDouble(out var value)
                    || !double.IsFinite(value))
                {
                    throw Wrong("a reading has no usable value");
                }

                readings.Add(new BodyReading(signal, ReadDay(entryDay, "a reading's day"), value));
            }
        }

        return new DecodedBodyBackup(BodyHistory.Create(installDay, readings), version, unread);
    }

    private static DateOnly ReadDay(JsonElement element, string what)
    {
        if (element.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(element.GetString(), DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return day;
        }

        throw Wrong($"{what} is not a yyyy-MM-dd date");
    }

    private static string Day(DateOnly day) => day.ToString(DayFormat, CultureInfo.InvariantCulture);

    private static BodyBackupFormatException Wrong(string problem) =>
        new($"The Body backup could not be read: {problem}.");
}
