using Constitution.Domain.Household;
using Constitution.Domain.Household.Lease;
using Constitution.Tests.Calendar;
using Storyworthtelling.Household;

namespace Constitution.Tests.HouseholdFile;

/// <summary>
/// One phone in a lease scenario: its install id and role, its own clock, and its own copy of the
/// household file. Sam's install is the owner's, Lee's the partner's (invented household). The phone
/// does exactly what the head will do around the decision: beat when due, ask, write what was asked.
/// </summary>
internal sealed class LeasePhone
{
    public LeasePhone(string install, RosterRole role)
    {
        Install = install;
        Role = role;
    }

    public string Install { get; }

    public RosterRole Role { get; }

    public HybridLogicalClock Clock { get; } = new();

    public HouseholdDocument Local { get; set; } = HouseholdDocument.Empty;

    public TimeSpan Lease { get; init; } = LeaseTerms.DefaultLeaseLength;

    /// <summary>Asks the question and writes nothing.</summary>
    public LeaseDecision Ask(DateTimeOffset now) =>
        PlanningLease.Decide(Local, Install, Role, now, LeaseRig.Zone, Lease);

    public void Beat(DateTimeOffset now) =>
        Local = LeaseWriter.Beat(Local, Clock, Install, Role, now, Lease);

    /// <summary>A pass: beat if it is due, ask, and do what was asked (a claim, or a release).</summary>
    public LeaseDecision Pass(DateTimeOffset now)
    {
        if (LeaseWriter.NeedsHeartbeat(Local, Install, now))
        {
            Beat(now);
        }

        var decision = Ask(now);
        Local = LeaseWriter.Apply(Local, decision, Clock, Install, Role, now, LeaseRig.Zone, Lease);
        return decision;
    }

    /// <summary>Takes in what the file holds.</summary>
    public void Read(HouseholdDocument file) => Local = HouseholdMerge.Merge(Local, file);
}

internal static class LeaseRig
{
    public const string Sam = "install-sam";
    public const string Lee = "install-lee";

    public static TimeZoneInfo Zone => InventedFamily.Zone;

    /// <summary>A 2026 wall-clock time on daylight saving time (UTC-4).</summary>
    public static DateTimeOffset Edt(int month, int day, int hour, int minute = 0) => InventedFamily.Edt(month, day, hour, minute);

    /// <summary>A 2026 wall-clock time on standard time (UTC-5).</summary>
    public static DateTimeOffset Est(int month, int day, int hour, int minute = 0) => InventedFamily.Est(month, day, hour, minute);

    public static TimeSpan Lease => LeaseTerms.DefaultLeaseLength;

    public static TimeSpan Settle => LeaseTerms.SettleWindow;

    /// <summary>Sunday 25 October 2026, 8 am on the household clock (daylight time; the clocks go back a week later, on the next Sunday).</summary>
    public static DateTimeOffset OpeningSunday => InventedFamily.Edt(10, 25, 8);

    public static (LeasePhone Owner, LeasePhone Partner) Phones() =>
        (new LeasePhone(Sam, RosterRole.Owner), new LeasePhone(Lee, RosterRole.Partner));

    /// <summary>Both phones read the same file: each takes in the other's copy.</summary>
    public static void Share(LeasePhone first, LeasePhone second)
    {
        var merged = HouseholdMerge.Merge(first.Local, second.Local);
        first.Local = merged;
        second.Local = merged;
    }

    public static long WeekOf(DateTimeOffset instant) => LeaseWeek.IndexOf(instant, Zone);

    /// <summary>A claim record written by hand, for the tests that need a stamp or a shape the writer would not make.</summary>
    public static HouseholdRecord ClaimRecord(string holder, long week, DateTimeOffset taken, HlcTimestamp stamp, string writer, TimeSpan? lease = null) =>
        HouseholdRecord.Write(
            LeaseKeys.ClaimKey(week),
            RecordSchemaVersion,
            new Dictionary<string, string>
            {
                ["holder"] = holder,
                ["role"] = "owner",
                ["week"] = week.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["taken"] = taken.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["lease"] = ((long)(lease ?? Lease).TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            stamp,
            writer);

    public static HouseholdRecord BeatRecord(string install, string role, DateTimeOffset beat, HlcTimestamp stamp, TimeSpan? lease = null) =>
        HouseholdRecord.Write(
            LeaseKeys.HeartbeatKey(install),
            RecordSchemaVersion,
            new Dictionary<string, string>
            {
                ["install"] = install,
                ["role"] = role,
                ["beat"] = beat.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["lease"] = ((long)(lease ?? Lease).TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            stamp,
            install);

    private const int RecordSchemaVersion = 1;
}
