using System.Text;
using System.Text.Json;

namespace Constitution.Domain.Placement;

/// <summary>
/// The durable record of which keys have been written to a calendar. It exists to answer one question the
/// calendar cannot: <i>was this event ever made?</i> A key that is written and no longer on the calendar was
/// deleted by the owner, which means the session is skipped; a key that was never written is just new. Without
/// the record those two look the same and a delete would be undone on the next run.
///
/// <para>It is only ever grown: nothing here removes a key, so a skip is remembered for good. Where it is kept
/// (preferences, a file) is the head's business behind <see cref="IWrittenKeyStore"/>.</para>
/// </summary>
public sealed class WrittenKeys
{
    private readonly HashSet<PlacementKey> set;

    private WrittenKeys(IEnumerable<PlacementKey> keys)
    {
        set = keys.ToHashSet();
        Keys = set.OrderBy(key => key.Value, StringComparer.Ordinal).ToList().AsReadOnly();
    }

    public static WrittenKeys Empty { get; } = new([]);

    /// <summary>Every key, once each, in a fixed order.</summary>
    public IReadOnlyList<PlacementKey> Keys { get; }

    public bool Contains(PlacementKey key) => set.Contains(key);

    /// <summary>This record with one more key. The record it is called on is unchanged.</summary>
    /// <exception cref="ArgumentException">The key is the default (empty) key.</exception>
    public WrittenKeys With(PlacementKey key) => With([key]);

    /// <summary>This record with more keys. The record it is called on is unchanged.</summary>
    /// <exception cref="ArgumentException">A key is the default (empty) key.</exception>
    public WrittenKeys With(IEnumerable<PlacementKey> more)
    {
        ArgumentNullException.ThrowIfNull(more);
        var added = more.ToList();
        if (added.Any(key => key.Value.Length == 0))
        {
            throw new ArgumentException("An empty key names no event, so it cannot be recorded as written.", nameof(more));
        }

        return added.All(set.Contains) ? this : new WrittenKeys(set.Concat(added));
    }
}

/// <summary>
/// What reading the record gave: the keys, or the reason it could not be read. The two are never confused. A
/// record that cannot be read is <b>not</b> an empty one, because treating it as empty would read every
/// deleted event as one that was never made and write them all back.
/// </summary>
public sealed class WrittenKeysRead
{
    private WrittenKeysRead(WrittenKeys? keys, string? unreadableBecause)
    {
        Keys = keys ?? WrittenKeys.Empty;
        UnreadableBecause = unreadableBecause;
    }

    /// <summary>The keys; empty when the record is unreadable.</summary>
    public WrittenKeys Keys { get; }

    /// <summary>Why the record could not be read, or null when it could.</summary>
    public string? UnreadableBecause { get; }

    public bool IsReadable => UnreadableBecause is null;

    public static WrittenKeysRead Of(WrittenKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return new WrittenKeysRead(keys, null);
    }

    public static WrittenKeysRead Unreadable(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An unreadable record has to say why it could not be read.", nameof(reason));
        }

        return new WrittenKeysRead(null, reason.Trim());
    }
}

/// <summary>
/// The record as text: <c>{"version":1,"written":["c1:...", ...]}</c>, keys sorted so the same record is the
/// same text. Nothing stored yet (null or blank) is an empty record; anything else that cannot be read, a
/// version this code does not know included, is unreadable.
/// </summary>
public static class WrittenKeysCodec
{
    private const int CurrentVersion = 1;
    private const string VersionField = "version";
    private const string WrittenField = "written";

    public static string Encode(WrittenKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionField, CurrentVersion);
            writer.WriteStartArray(WrittenField);
            foreach (var key in keys.Keys)
            {
                writer.WriteStringValue(key.Value);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static WrittenKeysRead Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return WrittenKeysRead.Of(WrittenKeys.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return Read(document.RootElement);
        }
        catch (JsonException error)
        {
            return WrittenKeysRead.Unreadable($"The record of written events is not valid JSON ({error.Message}).");
        }
    }

    private static WrittenKeysRead Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return WrittenKeysRead.Unreadable("The record of written events is not a JSON object.");
        }

        if (!root.TryGetProperty(VersionField, out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var number)
            || number != CurrentVersion)
        {
            return WrittenKeysRead.Unreadable($"The record of written events is not version {CurrentVersion}, so it was not read.");
        }

        if (!root.TryGetProperty(WrittenField, out var written) || written.ValueKind != JsonValueKind.Array)
        {
            return WrittenKeysRead.Unreadable("The record of written events has no list of keys.");
        }

        var keys = new List<PlacementKey>();
        foreach (var item in written.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !PlacementKey.TryFromStored(item.GetString(), out var key))
            {
                return WrittenKeysRead.Unreadable("The record of written events holds an entry that is not a key this app wrote.");
            }

            keys.Add(key);
        }

        return WrittenKeysRead.Of(WrittenKeys.Empty.With(keys));
    }
}

/// <summary>
/// Where the record is kept between runs. The head implements it over local storage. <see cref="Load"/> says
/// plainly when the record cannot be read; <see cref="Save"/> is never called with a record built on top of an
/// unreadable one, so a damaged record is left for a person to look at rather than overwritten.
/// </summary>
public interface IWrittenKeyStore
{
    WrittenKeysRead Load();

    void Save(WrittenKeys keys);
}
