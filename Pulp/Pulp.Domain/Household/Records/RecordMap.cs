namespace Pulp.Domain.Household.Records;

/// <summary>The schema the record maps write. A newer writer raises it; <see cref="HouseholdRecord"/> keeps the highest.</summary>
public static class RecordSchema
{
    public const int Version = 1;
}

/// <summary>
/// How one Domain value is kept as a household record: its frozen <see cref="Kind"/>, the id that
/// makes its key, and its fields as words. <see cref="FieldsOf"/> names <b>every</b> field this map
/// knows — an absent optional value is written as an empty string — so an edit clears what the value
/// no longer has while the fields only a newer writer knows are carried through by
/// <see cref="HouseholdRecord.Edit"/>.
/// </summary>
public abstract class RecordMap<T>
    where T : notnull
{
    public abstract string Kind { get; }

    public RecordKey KeyOf(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new RecordKey(Kind, IdOf(value));
    }

    /// <summary>The id within the kind; built with <see cref="RecordIds.Join"/> when it has several parts.</summary>
    protected abstract string IdOf(T value);

    public abstract IReadOnlyDictionary<string, string> FieldsOf(T value);

    /// <summary>The value a live record holds; throws <see cref="HouseholdRecordException"/> when it cannot be read.</summary>
    public abstract T Read(RecordReader record);
}

/// <summary>A record of a known kind that could not be read, and why.</summary>
public sealed record UnreadableRecord(RecordKey Key, string Reason);

/// <summary>The values of one kind, and the records of that kind that could not be read.</summary>
public sealed record RecordSet<T>(IReadOnlyList<T> Items, IReadOnlyList<UnreadableRecord> Unreadable);

/// <summary>A record whose fields do not hold what its kind needs — a missing id, or a word this build does not know.</summary>
public sealed class HouseholdRecordException : Exception
{
    public HouseholdRecordException(RecordKey key, string message)
        : base($"{key}: {message}") => Key = key;

    public RecordKey Key { get; }
}

/// <summary>
/// Puts typed values into a household document and reads them back. The only door between the
/// Domain's types and the record bag, so every writer follows the same schema-version rule.
/// </summary>
public static class HouseholdRecords
{
    /// <summary>
    /// The document with <paramref name="value"/> written at <paramref name="stamp"/>. A live record
    /// of the same key is <b>edited</b>, not replaced, so fields a newer writer added survive (plan
    /// choice P3). The stamp must be newer than the record it changes; a stale one throws rather than
    /// losing to the version it meant to replace.
    /// </summary>
    public static HouseholdDocument Put<T>(HouseholdDocument document, RecordMap<T> map, T value, HlcTimestamp stamp, string writer)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(map);
        var key = map.KeyOf(value);
        var fields = map.FieldsOf(value);
        var current = document.Find(key);
        var record = current is null
            ? HouseholdRecord.Write(key, RecordSchema.Version, fields, stamp, writer)
            : current.Edit(fields, RecordSchema.Version, stamp, writer);
        return document.With(record);
    }

    /// <summary>The document with the value's record deleted (a tombstone, so the delete outranks older copies).</summary>
    public static HouseholdDocument Remove<T>(HouseholdDocument document, RecordMap<T> map, T value, HlcTimestamp stamp, string writer)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(map);
        return Remove(document, map.KeyOf(value), stamp, writer);
    }

    /// <summary>The document with <paramref name="key"/> deleted. Deleting a deleted record changes nothing.</summary>
    public static HouseholdDocument Remove(HouseholdDocument document, RecordKey key, HlcTimestamp stamp, string writer)
    {
        ArgumentNullException.ThrowIfNull(document);
        var current = document.Find(key);
        if (current is { IsTombstone: true })
        {
            return document;
        }

        var tombstone = current is null
            ? HouseholdRecord.Tombstone(key, RecordSchema.Version, stamp, writer)
            : current.Delete(stamp, writer);
        return document.With(tombstone);
    }

    /// <summary>
    /// Every live record of the map's kind, read. A record that cannot be read — most likely one a
    /// newer writer filled with a word this build does not know — is reported in
    /// <see cref="RecordSet{T}.Unreadable"/> rather than dropped silently or allowed to fail the rest.
    /// </summary>
    public static RecordSet<T> Read<T>(HouseholdDocument document, RecordMap<T> map)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(map);
        var items = new List<T>();
        var unreadable = new List<UnreadableRecord>();
        foreach (var record in document.LiveRecords.Where(record => record.Key.Kind == map.Kind))
        {
            try
            {
                items.Add(map.Read(new RecordReader(record)));
            }
            catch (HouseholdRecordException exception)
            {
                unreadable.Add(new UnreadableRecord(record.Key, exception.Message));
            }
        }

        return new RecordSet<T>(items, unreadable);
    }
}
