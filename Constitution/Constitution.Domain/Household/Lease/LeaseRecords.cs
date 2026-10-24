using System.Globalization;
using Constitution.Domain.Household.Records;
using Storyworthtelling.Household;

namespace Constitution.Domain.Household.Lease;

/// <summary>One install's sign of life: who, in what role, when it last spoke, and the lease it was running on.</summary>
public sealed record LeaseHeartbeat(string Install, RosterRole Role, DateTimeOffset Beat, TimeSpan Lease);

/// <summary>One install's claim on one week: who holds it, when it was taken, and the lease it was taken under.</summary>
public sealed record LeaseClaim(string Holder, RosterRole Role, long WeekIndex, DateTimeOffset Taken, TimeSpan Lease);

/// <summary>
/// The record keys the lease writes, both under the frozen <c>planning-lease</c> kind. A <b>heartbeat</b>
/// is keyed by its install, so only that install ever writes it and two installs' beats can never
/// overwrite each other; a <b>claim</b> is keyed by its week, so two claims on one week are one key and the
/// library's total order (latest stamp, then the larger writer id) leaves exactly one of them standing.
/// The ids are built with <see cref="RecordIds.Join"/>, so an install id holding the separator cannot
/// share a key with another.
/// </summary>
public static class LeaseKeys
{
    internal const string HeartbeatPart = "beat";
    internal const string ClaimPart = "week";

    public static RecordKey HeartbeatKey(string install)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(install);
        return new RecordKey(ConstitutionKinds.PlanningLease, RecordIds.Join(HeartbeatPart, install));
    }

    public static RecordKey ClaimKey(long weekIndex) =>
        new(ConstitutionKinds.PlanningLease, RecordIds.Join(ClaimPart, weekIndex.ToString(CultureInfo.InvariantCulture)));

    internal static bool IsHeartbeat(RecordKey key) => HasPrefix(key, HeartbeatPart);

    internal static bool IsClaim(RecordKey key) => HasPrefix(key, ClaimPart);

    private static bool HasPrefix(RecordKey key, string part) =>
        key.Kind == ConstitutionKinds.PlanningLease && key.Id.StartsWith(part + RecordIds.Separator, StringComparison.Ordinal);
}

/// <summary>Shared number and role reading for the two lease record shapes. Anything unreadable names the field and never repeats the value.</summary>
internal static class LeaseFields
{
    public static readonly FieldWords<RosterRole> Roles = new(new Dictionary<RosterRole, string>
    {
        [RosterRole.Owner] = "owner",
        [RosterRole.Partner] = "partner",
    });

    public static string Millis(DateTimeOffset instant) => instant.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    public static string Millis(TimeSpan span) => ((long)span.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);

    public static long Whole(RecordReader record, string name) =>
        long.TryParse(record.Text(name), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw record.Fail(name, "is not a whole number");

    public static DateTimeOffset Instant(RecordReader record, string name)
    {
        var millis = Whole(record, name);
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(millis);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw record.Fail(name, "is not a time the calendar holds");
        }
    }

    public static TimeSpan Length(RecordReader record, string name)
    {
        var millis = Whole(record, name);
        return millis > 0 && millis <= TimeSpan.MaxValue.TotalMilliseconds
            ? TimeSpan.FromMilliseconds(millis)
            : throw record.Fail(name, "is not a length of time");
    }
}

internal sealed class LeaseHeartbeatMap : RecordMap<LeaseHeartbeat>
{
    public override string Kind => ConstitutionKinds.PlanningLease;

    protected override string IdOf(LeaseHeartbeat value) => RecordIds.Join(LeaseKeys.HeartbeatPart, value.Install);

    public override IReadOnlyDictionary<string, string> FieldsOf(LeaseHeartbeat value) =>
        new FieldWriter()
            .Text("install", value.Install)
            .Word("role", LeaseFields.Roles, value.Role)
            .Text("beat", LeaseFields.Millis(value.Beat))
            .Text("lease", LeaseFields.Millis(value.Lease))
            .Fields;

    public override LeaseHeartbeat Read(RecordReader record)
    {
        var value = new LeaseHeartbeat(
            record.Text("install"),
            record.Word("role", LeaseFields.Roles),
            LeaseFields.Instant(record, "beat"),
            LeaseFields.Length(record, "lease"));
        return KeyOf(value) == record.Key ? value : throw record.Fail("install", "does not belong to this record's key");
    }
}

internal sealed class LeaseClaimMap : RecordMap<LeaseClaim>
{
    public override string Kind => ConstitutionKinds.PlanningLease;

    protected override string IdOf(LeaseClaim value) =>
        RecordIds.Join(LeaseKeys.ClaimPart, value.WeekIndex.ToString(CultureInfo.InvariantCulture));

    public override IReadOnlyDictionary<string, string> FieldsOf(LeaseClaim value) =>
        new FieldWriter()
            .Text("holder", value.Holder)
            .Word("role", LeaseFields.Roles, value.Role)
            .Text("week", value.WeekIndex.ToString(CultureInfo.InvariantCulture))
            .Text("taken", LeaseFields.Millis(value.Taken))
            .Text("lease", LeaseFields.Millis(value.Lease))
            .Fields;

    public override LeaseClaim Read(RecordReader record)
    {
        var value = new LeaseClaim(
            record.Text("holder"),
            record.Word("role", LeaseFields.Roles),
            LeaseFields.Whole(record, "week"),
            LeaseFields.Instant(record, "taken"),
            LeaseFields.Length(record, "lease"));
        return KeyOf(value) == record.Key ? value : throw record.Fail("week", "does not belong to this record's key");
    }
}

/// <summary>The lease's two maps, one schema each, shared by the writer and the reader.</summary>
internal static class LeaseMaps
{
    public static LeaseHeartbeatMap Heartbeat { get; } = new();

    public static LeaseClaimMap Claim { get; } = new();
}

/// <summary>
/// The lease records of a household document, read. A record that cannot be read is listed, never dropped
/// and never fatal to the rest; the decision treats an unreadable record that bears on it as a reason not
/// to plan. Records of the lease's kind that this build does not recognise (a newer writer's) are left
/// alone, neither read nor reported.
/// </summary>
public sealed class LeaseView
{
    private LeaseView(
        IReadOnlyDictionary<string, LeaseHeartbeat> heartbeats,
        IReadOnlyDictionary<long, LeaseClaim> claims,
        IReadOnlyList<UnreadableRecord> unreadableHeartbeats,
        IReadOnlyList<UnreadableRecord> unreadableClaims)
    {
        Heartbeats = heartbeats;
        Claims = claims;
        UnreadableHeartbeats = unreadableHeartbeats;
        UnreadableClaims = unreadableClaims;
    }

    /// <summary>Each install's latest heartbeat, by install id (compared without case).</summary>
    public IReadOnlyDictionary<string, LeaseHeartbeat> Heartbeats { get; }

    /// <summary>The standing claim on each week, by week index.</summary>
    public IReadOnlyDictionary<long, LeaseClaim> Claims { get; }

    public IReadOnlyList<UnreadableRecord> UnreadableHeartbeats { get; }

    public IReadOnlyList<UnreadableRecord> UnreadableClaims { get; }

    public IReadOnlyList<UnreadableRecord> UnreadableRecords => [.. UnreadableHeartbeats, .. UnreadableClaims];

    public static LeaseView Read(HouseholdDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var heartbeats = new Dictionary<string, LeaseHeartbeat>(StringComparer.OrdinalIgnoreCase);
        var claims = new Dictionary<long, LeaseClaim>();
        var unreadableHeartbeats = new List<UnreadableRecord>();
        var unreadableClaims = new List<UnreadableRecord>();

        foreach (var record in document.LiveRecords)
        {
            if (LeaseKeys.IsHeartbeat(record.Key))
            {
                try
                {
                    var beat = LeaseMaps.Heartbeat.Read(new RecordReader(record));
                    heartbeats[beat.Install] = beat;
                }
                catch (HouseholdRecordException exception)
                {
                    unreadableHeartbeats.Add(new UnreadableRecord(record.Key, exception.Message));
                }
            }
            else if (LeaseKeys.IsClaim(record.Key))
            {
                try
                {
                    var claim = LeaseMaps.Claim.Read(new RecordReader(record));
                    claims[claim.WeekIndex] = claim;
                }
                catch (HouseholdRecordException exception)
                {
                    unreadableClaims.Add(new UnreadableRecord(record.Key, exception.Message));
                }
            }
        }

        return new LeaseView(heartbeats, claims, unreadableHeartbeats, unreadableClaims);
    }
}
