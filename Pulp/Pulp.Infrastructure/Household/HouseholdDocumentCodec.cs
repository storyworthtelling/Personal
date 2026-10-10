using System.Text;
using System.Text.Json;
using Pulp.Domain.Household;

namespace Pulp.Infrastructure.Household;

/// <summary>A household file this build cannot read: damaged, not a household, or from a newer format.</summary>
public sealed class HouseholdFormatException : Exception
{
    public HouseholdFormatException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// The household document ⇄ JSON (finding §2.7). One object, <c>format</c> plus <c>records</c>; each
/// record carries its kind, id, schema, stamp (wall + counter), writer, whether it is a tombstone,
/// and its fields as strings. Records are written in key order, so an unchanged household is
/// byte-identical text.
///
/// <para><b>Format versions:</b> a file stating a format newer than
/// <see cref="HouseholdDocument.FormatVersion"/> is refused loudly — read as best it could, merged
/// and written back, it would be rewritten in the old shape and the newer writer's work lost. New
/// <i>fields</i> never need a new format (plan choice P3); they ride inside a record's fields and
/// survive any older writer. A newer writer that needs a property outside the fields must raise the
/// format, because this codec does not carry unknown record properties through.</para>
///
/// <para>Written by hand over <see cref="Utf8JsonWriter"/> and <see cref="JsonDocument"/> rather than
/// reflection, so the trimmed Android build cannot strip a property the file depends on.</para>
/// </summary>
public static class HouseholdDocumentCodec
{
    public static string Serialize(HouseholdDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", HouseholdDocument.FormatVersion);
            writer.WriteStartArray("records");
            foreach (var record in document.Records)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", record.Key.Kind);
                writer.WriteString("id", record.Key.Id);
                writer.WriteNumber("schema", record.SchemaVersion);
                writer.WriteNumber("wall", record.Stamp.WallMillis);
                writer.WriteNumber("counter", record.Stamp.Counter);
                writer.WriteString("writer", record.Writer);
                writer.WriteBoolean("deleted", record.IsTombstone);
                writer.WriteStartObject("fields");
                foreach (var field in record.Fields)
                {
                    writer.WriteString(field.Key, field.Value);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static HouseholdDocument Deserialize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new HouseholdFormatException("The household file is empty; it holds no format and no records.");
        }

        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new HouseholdFormatException("The household file is not JSON: " + exception.Message, exception);
        }

        using (json)
        {
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new HouseholdFormatException($"The household file is a JSON {root.ValueKind}, not an object.");
            }

            if (!root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Number || !format.TryGetInt32(out var version))
            {
                throw new HouseholdFormatException("The household file states no format version.");
            }

            if (version > HouseholdDocument.FormatVersion)
            {
                throw new HouseholdFormatException(
                    $"The household file is format {version}, written by a newer Pulp; this build reads format {HouseholdDocument.FormatVersion} and will not rewrite it in the older shape. Update Pulp on this device.");
            }

            if (version < 1)
            {
                throw new HouseholdFormatException($"The household file states format {version}, which no Pulp ever wrote.");
            }

            if (!root.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
            {
                throw new HouseholdFormatException("The household file has no records list.");
            }

            var document = HouseholdDocument.Empty;
            var index = 0;
            foreach (var element in records.EnumerateArray())
            {
                document = document.With(ReadRecord(element, index));
                index++;
            }

            return document;
        }
    }

    private static HouseholdRecord ReadRecord(JsonElement element, int index)
    {
        var where = $"record {index}";
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new HouseholdFormatException($"The household file's {where} is not an object.");
        }

        try
        {
            var key = new RecordKey(Text(element, "kind", where), Text(element, "id", where));
            where = $"record {key}";
            var schema = (int)Number(element, "schema", where);
            var stamp = new HlcTimestamp(Number(element, "wall", where), (int)Number(element, "counter", where));
            var writer = Text(element, "writer", where);
            var deleted = element.TryGetProperty("deleted", out var flag) && flag.ValueKind == JsonValueKind.True;
            if (deleted)
            {
                return HouseholdRecord.Tombstone(key, schema, stamp, writer);
            }

            if (!element.TryGetProperty("fields", out var fieldsElement) || fieldsElement.ValueKind != JsonValueKind.Object)
            {
                throw new HouseholdFormatException($"The household file's {where} has no fields object.");
            }

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in fieldsElement.EnumerateObject())
            {
                if (field.Value.ValueKind != JsonValueKind.String)
                {
                    throw new HouseholdFormatException($"The household file's {where} has field '{field.Name}' as a {field.Value.ValueKind}; fields are strings.");
                }

                fields[field.Name] = field.Value.GetString()!;
            }

            return HouseholdRecord.Write(key, schema, fields, stamp, writer);
        }
        catch (ArgumentException exception)
        {
            // Domain refused a value (a negative stamp, a blank writer, a schema below one).
            throw new HouseholdFormatException($"The household file's {where} is not a valid record: {exception.Message}", exception);
        }
    }

    private static string Text(JsonElement element, string name, string where) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new HouseholdFormatException($"The household file's {where} has no '{name}'.");

    private static long Number(JsonElement element, string name, string where) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : throw new HouseholdFormatException($"The household file's {where} has no whole-number '{name}'.");
}
