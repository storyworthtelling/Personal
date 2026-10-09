namespace Pulp.Domain.Household;

/// <summary>
/// The household file's contents as Domain sees them: one <see cref="HouseholdRecord"/> per key,
/// tombstones included. Immutable — every change returns a new document — and indifferent to how it
/// is stored; the JSON codec and the encryption belong to Infrastructure.
///
/// <para>Tombstones are kept, never dropped: a tombstone removed from one device's copy would let
/// the other device's older live copy come back on the next merge. Pruning them safely needs every
/// writer to have seen the delete, which no rule here knows; it is left for when the file's size
/// says it matters.</para>
/// </summary>
public sealed class HouseholdDocument
{
    /// <summary>
    /// The version of the document's shape (records keyed, stamped, written by an install). The
    /// TV phase adds fields to records under <see cref="HouseholdRecord.SchemaVersion"/> instead of
    /// raising this (plan choice P3).
    /// </summary>
    public const int FormatVersion = 1;

    private readonly IReadOnlyDictionary<RecordKey, HouseholdRecord> records;

    private HouseholdDocument(IReadOnlyDictionary<RecordKey, HouseholdRecord> records) => this.records = records;

    public static HouseholdDocument Empty { get; } = new(new Dictionary<RecordKey, HouseholdRecord>());

    /// <summary>Every record, tombstones included, ordered by key.</summary>
    public IReadOnlyList<HouseholdRecord> Records =>
        records.Values.OrderBy(record => record.Key.Value, StringComparer.Ordinal).ToList();

    /// <summary>The records that are not deleted, ordered by key.</summary>
    public IReadOnlyList<HouseholdRecord> LiveRecords => Records.Where(record => !record.IsTombstone).ToList();

    /// <summary>
    /// The newest stamp in the document. A device witnesses it after reading the file so that its
    /// next edit outranks everything it read (<see cref="HybridLogicalClock.Witness"/>).
    /// </summary>
    public HlcTimestamp MaxStamp =>
        records.Values.Select(record => record.Stamp).DefaultIfEmpty(HlcTimestamp.Zero).Max();

    /// <summary>The record for a key, tombstone or not; null when the key was never written.</summary>
    public HouseholdRecord? Find(RecordKey key) => records.GetValueOrDefault(key);

    /// <summary>The record for a key if it exists and is not deleted.</summary>
    public HouseholdRecord? Live(RecordKey key) => Find(key) is { IsTombstone: false } record ? record : null;

    /// <summary>This document with one record merged in: it replaces the current one only if it outranks it.</summary>
    public HouseholdDocument With(HouseholdRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var current = Find(record.Key);
        if (current is not null && !record.Outranks(current))
        {
            return this;
        }

        var next = new Dictionary<RecordKey, HouseholdRecord>(records) { [record.Key] = record };
        return new HouseholdDocument(next);
    }

    /// <summary>Whether both documents hold exactly the same records.</summary>
    public bool ContentEquals(HouseholdDocument other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return records.Count == other.records.Count
            && records.All(pair => other.Find(pair.Key) is { } theirs && pair.Value.ContentEquals(theirs));
    }
}
