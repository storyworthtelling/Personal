using System.Globalization;
using System.Text.Json;
using Constitution.Domain.Ink;

namespace Constitution.Domain.Workouts;

/// <summary>
/// How a session ended. <see cref="Unsettled"/> is not a verdict: it is a read that cannot say (a page not read, a
/// question waiting to be answered), so nothing is recorded and nothing is held against the owner.
/// </summary>
public enum SessionOutcome
{
    Done,

    Skipped,

    Unsettled,
}

/// <summary>One session that has ended: who, which day, what kind, and whether it was done or skipped.</summary>
public sealed record SessionRecord(string Person, DateOnly Day, SessionKind Kind, SessionOutcome Outcome);

/// <summary>
/// A session as the pen sees it: whose, which day and kind, and the ids of every box its pages carry (the workout box,
/// each exercise page's Done box, the assessment's number and circle boxes), in the ids the resolver reports them under.
/// </summary>
public sealed record SessionMarks(string Person, DateOnly Day, SessionKind Kind, IReadOnlyList<string> TargetIds);

/// <summary>One person's week as the digest reads it: the days a session was done and the days one was skipped, in order.</summary>
public sealed record WeekRecord(string Person, DateOnly WeekStart, IReadOnlyList<DateOnly> DoneDays, IReadOnlyList<DateOnly> SkippedDays);

/// <summary>
/// End of day for one session (C-5.2.1): any mark on any of its pages is done (D18, a scribble included); every page read
/// and every box empty is skipped; and anything unread or unanswered is <see cref="SessionOutcome.Unsettled"/>, because
/// a page that was not read is never silence (the resolver's own rule) and a skip is not to be recorded from a failed scan.
///
/// <para><b>This says nothing.</b> The result is a verdict and nothing else: no notice, no voiced line, no message. The
/// plan easing is the whole consequence, and it happens through <see cref="WorkoutFrequency"/>.</para>
/// </summary>
public static class DayClose
{
    public static SessionOutcome Decide(SessionMarks marks, IEnumerable<InkResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(resolutions);
        if (string.IsNullOrWhiteSpace(marks.Person))
        {
            throw new ArgumentException("A session belongs to someone.", nameof(marks));
        }

        if (marks.TargetIds is null || marks.TargetIds.Count == 0)
        {
            throw new ArgumentException("A session with no boxes cannot be read as done or skipped.", nameof(marks));
        }

        var ours = marks.TargetIds.ToHashSet(StringComparer.Ordinal);
        var relevant = resolutions.Where(item => ours.Contains(item.SubjectId)).ToList();

        if (relevant.Any(item => item is InkResolution.Ticked or InkResolution.PageDone or InkResolution.Written or InkResolution.Circled))
        {
            return SessionOutcome.Done;
        }

        if (relevant.Any(item => item is InkResolution.NotRead or InkResolution.NeedsAsking or InkResolution.Undecided))
        {
            return SessionOutcome.Unsettled;
        }

        return relevant.Any(item => item is InkResolution.Skipped) ? SessionOutcome.Skipped : SessionOutcome.Unsettled;
    }
}

/// <summary>What <see cref="SkipLedger.FromJson"/> found: the ledger, how many entries could not be read, and whether the whole file could not.</summary>
public sealed record SkipLedgerRead(SkipLedger Ledger, int UnreadableEntries, bool WholeFileUnreadable);

/// <summary>
/// The days that ended, one record per person per day (the planner never plans two sessions on a day). Immutable:
/// <see cref="Settle"/> returns the next ledger. It turns the ink of a finished day into the inputs the planner already
/// reads (<see cref="RecoveryInputs"/>), the last done day the assessment rule reads (<see cref="TrainingHistory"/>), and
/// the done/skipped days the digest reads (<see cref="WeekRecord"/>).
///
/// <para><b>Gentle walks are kept but not counted.</b> <see cref="WorkoutFrequency"/> says Sundays are extra, so a
/// skipped Sunday walk does not ease the week and a done one does not earn a fourth session; both still appear in the
/// digest and in <see cref="LastDoneDay"/>.</para>
///
/// <para><b>An answer is only replaced by another answer.</b> A later read that cannot settle (<see cref="SessionOutcome.Unsettled"/>)
/// leaves the record alone; a later read that can replaces it, so a page scanned late corrects an earlier skip.</para>
/// </summary>
public sealed class SkipLedger
{
    private const int Version = 1;
    private const int DaysInWeek = 7;
    private const string DayFormat = "yyyy-MM-dd";

    private readonly IReadOnlyList<SessionRecord> records;

    private SkipLedger(IReadOnlyList<SessionRecord> records)
    {
        this.records = records;
    }

    public static SkipLedger Empty { get; } = new([]);

    /// <summary>Every settled session, earliest day first, then by person.</summary>
    public IReadOnlyList<SessionRecord> Records => records;

    public SkipLedger Settle(SessionMarks marks, IEnumerable<InkResolution> resolutions)
    {
        var outcome = DayClose.Decide(marks, resolutions);
        return outcome == SessionOutcome.Unsettled ? this : With(new SessionRecord(marks.Person.Trim(), marks.Day, marks.Kind, outcome));
    }

    /// <summary>How the person's session on that day ended, or null when none has been settled.</summary>
    public SessionOutcome? OutcomeOf(string person, DateOnly day) =>
        records.FirstOrDefault(item => item.Day == day && Same(item.Person, person))?.Outcome;

    /// <summary>The latest day the person did any session, or null when they never have.</summary>
    public DateOnly? LastDoneDay(string person) =>
        records.Where(item => item.Outcome == SessionOutcome.Done && Same(item.Person, person)).Select(item => (DateOnly?)item.Day).Max();

    /// <summary>The digest's view of one person's week starting <paramref name="weekStart"/>.</summary>
    public WeekRecord Week(string person, DateOnly weekStart)
    {
        var inWeek = InRange(person, weekStart, weekStart.AddDays(DaysInWeek - 1)).ToList();
        return new WeekRecord(
            person,
            weekStart,
            inWeek.Where(item => item.Outcome == SessionOutcome.Done).Select(item => item.Day).ToList(),
            inWeek.Where(item => item.Outcome == SessionOutcome.Skipped).Select(item => item.Day).ToList());
    }

    /// <summary>
    /// What the planner is told about the person for the week starting <paramref name="nextWeekStart"/>: the sessions done
    /// and skipped in the seven days before it (gentle walks left out), null for "done" when nothing was settled that
    /// week at all (no history), and the latest hard session done before the week.
    /// </summary>
    public RecoveryInputs RecoveryFor(string person, DateOnly nextWeekStart)
    {
        var lastWeek = InRange(person, nextWeekStart.AddDays(-DaysInWeek), nextWeekStart.AddDays(-1))
            .Where(item => item.Kind != SessionKind.GentleWalk)
            .ToList();
        var lastHard = records
            .Where(item => Same(item.Person, person) && item.Outcome == SessionOutcome.Done && SessionKinds.IsHard(item.Kind) && item.Day < nextWeekStart)
            .Select(item => (DateOnly?)item.Day)
            .Max();

        return new RecoveryInputs
        {
            SessionsDoneLastWeek = lastWeek.Count == 0 ? null : lastWeek.Count(item => item.Outcome == SessionOutcome.Done),
            SessionsSkippedLastWeek = lastWeek.Count(item => item.Outcome == SessionOutcome.Skipped),
            LastHardSessionDay = lastHard,
        };
    }

    // ---- the stored file ----

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartArray("records");
            foreach (var item in records)
            {
                writer.WriteStartObject();
                writer.WriteString("person", item.Person);
                writer.WriteString("day", item.Day.ToString(DayFormat, CultureInfo.InvariantCulture));
                writer.WriteString("kind", KindName(item.Kind));
                writer.WriteString("outcome", OutcomeName(item.Outcome));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Reads the stored file. Nothing stored is an empty ledger with no problem (a fresh install); a file that cannot be read
    /// as a ledger at all comes back empty with <see cref="SkipLedgerRead.WholeFileUnreadable"/> set, so the caller can raise
    /// a notice and not write over it; an entry that cannot be read is skipped and counted, and the rest are kept.
    /// </summary>
    public static SkipLedgerRead FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SkipLedgerRead(Empty, 0, false);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number)
                || number != Version
                || !root.TryGetProperty("records", out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return new SkipLedgerRead(Empty, 0, true);
            }

            var ledger = Empty;
            var unreadable = 0;
            foreach (var entry in array.EnumerateArray())
            {
                if (TryRecord(entry) is { } record)
                {
                    ledger = ledger.With(record);
                }
                else
                {
                    unreadable++;
                }
            }

            return new SkipLedgerRead(ledger, unreadable, false);
        }
        catch (JsonException)
        {
            // Not JSON at all. The caller is told so, and the file is left for them to keep or repair; it is not read as "nothing happened".
            return new SkipLedgerRead(Empty, 0, true);
        }
    }

    private static SessionRecord? TryRecord(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || Text(entry, "person") is not { } person
            || string.IsNullOrWhiteSpace(person)
            || Text(entry, "day") is not { } dayText
            || !DateOnly.TryParseExact(dayText, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            || KindOf(Text(entry, "kind")) is not { } kind
            || OutcomeOf(Text(entry, "outcome")) is not { } outcome)
        {
            return null;
        }

        return new SessionRecord(person.Trim(), day, kind, outcome);
    }

    private static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string KindName(SessionKind kind) => kind switch
    {
        SessionKind.Strength => "strength",
        SessionKind.OutdoorCardio => "outdoor-cardio",
        SessionKind.Treadmill => "treadmill",
        SessionKind.Mobility => "mobility",
        SessionKind.GentleWalk => "gentle-walk",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This kind of session has no stored name."),
    };

    private static SessionKind? KindOf(string? name) => name switch
    {
        "strength" => SessionKind.Strength,
        "outdoor-cardio" => SessionKind.OutdoorCardio,
        "treadmill" => SessionKind.Treadmill,
        "mobility" => SessionKind.Mobility,
        "gentle-walk" => SessionKind.GentleWalk,
        _ => null,
    };

    private static string OutcomeName(SessionOutcome outcome) => outcome switch
    {
        SessionOutcome.Done => "done",
        SessionOutcome.Skipped => "skipped",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Only a done or skipped session is stored."),
    };

    private static SessionOutcome? OutcomeOf(string? name) => name switch
    {
        "done" => SessionOutcome.Done,
        "skipped" => SessionOutcome.Skipped,
        _ => null,
    };

    // ---- helpers ----

    private static bool Same(string left, string right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private IEnumerable<SessionRecord> InRange(string person, DateOnly first, DateOnly last) =>
        records.Where(item => Same(item.Person, person) && item.Day >= first && item.Day <= last);

    private SkipLedger With(SessionRecord record) =>
        new(records
            .Where(item => !(item.Day == record.Day && Same(item.Person, record.Person)))
            .Append(record)
            .OrderBy(item => item.Day)
            .ThenBy(item => item.Person, StringComparer.OrdinalIgnoreCase)
            .ToList());
}
