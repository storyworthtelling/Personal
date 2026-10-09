namespace Pulp.Domain.Household;

/// <summary>
/// The address of one record in the household file: a kind ("playlist", "watched", "favorite"…) and
/// an id within it. Compared without case, like the ids it is built from (<c>Pulp.Domain.Core</c>),
/// and normalised to lower case so the stored spelling is single-valued — two spellings of one key
/// would otherwise be two records that never merge.
/// </summary>
public readonly record struct RecordKey
{
    public const char Separator = '/';

    public RecordKey(string kind, string id)
    {
        if (string.IsNullOrWhiteSpace(kind) || kind.Contains(Separator))
        {
            throw new ArgumentException("A record kind needs a value and cannot contain '" + Separator + "'.", nameof(kind));
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A record id needs a value; an empty id would collide every record of its kind.", nameof(id));
        }

        Kind = kind.ToLowerInvariant();
        Id = id.ToLowerInvariant();
    }

    public string Kind { get; }

    public string Id { get; }

    /// <summary>The key as one string, kind first: <c>kind/id</c>.</summary>
    public string Value => Kind + Separator + Id;

    public override string ToString() => Value;
}

/// <summary>
/// One entry of the household file (finding §2.7, D46): a key, the fields it holds, a hybrid-logical
/// clock stamp, the install that wrote it, and a schema version. A delete is a <b>tombstone</b> — a
/// record with no fields that still carries its stamp — so a delete outranks every older write of
/// the same key instead of disappearing and letting an older copy come back.
///
/// <para><b>Fields are an open bag of strings</b>, which is what keeps Domain serialisation-agnostic
/// and what lets the TV phase add fields without a migration (plan choice P3): <see cref="Edit"/>
/// starts from every field already there, so a writer on an older schema changes what it knows and
/// carries the rest through untouched. The version only ever rises.</para>
///
/// <para>Immutable. Two records of one key are ranked by <see cref="Outranks"/>, a total order, which
/// is what makes the merge commutative, associative and idempotent.</para>
/// </summary>
public sealed class HouseholdRecord
{
    private static readonly IReadOnlyDictionary<string, string> NoFields =
        new SortedDictionary<string, string>(StringComparer.Ordinal);

    private HouseholdRecord(
        RecordKey key,
        int schemaVersion,
        IReadOnlyDictionary<string, string> fields,
        HlcTimestamp stamp,
        string writer,
        bool isTombstone)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        if (string.IsNullOrWhiteSpace(writer))
        {
            throw new ArgumentException("A record needs its writer's install id; without it two writers' ties cannot be broken.", nameof(writer));
        }

        Key = key;
        SchemaVersion = schemaVersion;
        Fields = fields;
        Stamp = stamp;
        Writer = writer;
        IsTombstone = isTombstone;
    }

    public RecordKey Key { get; }

    /// <summary>The newest schema any writer of this record has known. It only rises.</summary>
    public int SchemaVersion { get; }

    /// <summary>The record's fields, ordered by name. Empty on a tombstone.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; }

    public HlcTimestamp Stamp { get; }

    /// <summary>The install id of the device that wrote this version of the record.</summary>
    public string Writer { get; }

    public bool IsTombstone { get; }

    /// <summary>A new record, or a whole replacement of one.</summary>
    public static HouseholdRecord Write(
        RecordKey key,
        int schemaVersion,
        IReadOnlyDictionary<string, string> fields,
        HlcTimestamp stamp,
        string writer)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new HouseholdRecord(key, schemaVersion, Copy(fields), stamp, writer, isTombstone: false);
    }

    /// <summary>A delete of a key this writer has no copy of.</summary>
    public static HouseholdRecord Tombstone(RecordKey key, int schemaVersion, HlcTimestamp stamp, string writer) =>
        new(key, schemaVersion, NoFields, stamp, writer, isTombstone: true);

    /// <summary>
    /// This record with <paramref name="changes"/> laid over its fields — every field not named in
    /// the changes is kept, including ones this writer's schema does not know. Editing a tombstone
    /// starts from no fields.
    /// </summary>
    public HouseholdRecord Edit(
        IReadOnlyDictionary<string, string> changes,
        int writerSchemaVersion,
        HlcTimestamp stamp,
        string writer)
    {
        ArgumentNullException.ThrowIfNull(changes);
        RequireNewer(stamp);

        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!IsTombstone)
        {
            foreach (var field in Fields)
            {
                fields[field.Key] = field.Value;
            }
        }

        foreach (var change in changes)
        {
            fields[change.Key] = change.Value;
        }

        return new HouseholdRecord(Key, Math.Max(SchemaVersion, writerSchemaVersion), fields, stamp, writer, isTombstone: false);
    }

    /// <summary>A tombstone for this record. The schema version is kept so a delete never lowers it.</summary>
    public HouseholdRecord Delete(HlcTimestamp stamp, string writer)
    {
        RequireNewer(stamp);
        return new HouseholdRecord(Key, SchemaVersion, NoFields, stamp, writer, isTombstone: true);
    }

    /// <summary>
    /// Whether this record wins over <paramref name="other"/> (same key) in a merge. Latest stamp
    /// first; then the larger writer id (ordinal), so two devices that stamped the same moment
    /// agree on one winner; then a delete over a write; then — only reachable if one writer stamped
    /// two different versions identically, which a correct clock never does — the larger schema and
    /// the larger field content, so even that case resolves the same way on every device.
    /// </summary>
    public bool Outranks(HouseholdRecord other) => Rank(this, other) > 0;

    /// <summary>Same key, stamp, writer, version, kind and fields.</summary>
    public bool ContentEquals(HouseholdRecord other) => Rank(this, other) == 0 && Key == other.Key;

    private static int Rank(HouseholdRecord left, HouseholdRecord right)
    {
        var order = left.Stamp.CompareTo(right.Stamp);
        if (order == 0)
        {
            order = string.CompareOrdinal(left.Writer, right.Writer);
        }

        if (order == 0)
        {
            order = left.IsTombstone.CompareTo(right.IsTombstone);
        }

        if (order == 0)
        {
            order = left.SchemaVersion.CompareTo(right.SchemaVersion);
        }

        return order != 0 ? order : CompareFields(left.Fields, right.Fields);
    }

    private static int CompareFields(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        // Both are ordinal-sorted dictionaries, so walking them side by side is a stable comparison.
        using var leftFields = left.GetEnumerator();
        using var rightFields = right.GetEnumerator();
        while (true)
        {
            var leftMore = leftFields.MoveNext();
            var rightMore = rightFields.MoveNext();
            if (!leftMore || !rightMore)
            {
                return leftMore.CompareTo(rightMore);
            }

            var order = string.CompareOrdinal(leftFields.Current.Key, rightFields.Current.Key);
            if (order == 0)
            {
                order = string.CompareOrdinal(leftFields.Current.Value, rightFields.Current.Value);
            }

            if (order != 0)
            {
                return order;
            }
        }
    }

    private void RequireNewer(HlcTimestamp stamp)
    {
        if (stamp <= Stamp)
        {
            throw new ArgumentException(
                $"A change to {Key} stamped {stamp} is not newer than the record's own {Stamp}; it would lose to the version it edits and vanish. Witness the record's stamp before stamping the change.",
                nameof(stamp));
        }
    }

    private static IReadOnlyDictionary<string, string> Copy(IReadOnlyDictionary<string, string> fields)
    {
        var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            copy[field.Key] = field.Value ?? throw new ArgumentException("Field '" + field.Key + "' has no value.", nameof(fields));
        }

        return copy;
    }
}
