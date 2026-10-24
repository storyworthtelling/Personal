using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Codex.Domain.Music;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Body;

namespace Constitution.Infrastructure.Workouts;

/// <summary>One session of the placed week, as the Workouts tab lists it. Words and numbers only: no dose, no load, no page and no voiced line.</summary>
public sealed record StoredSession(
    [property: JsonPropertyName("person")] string Person,
    [property: JsonPropertyName("day")] DateOnly Day,
    [property: JsonPropertyName("start")] DateTimeOffset Start,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("minutes")] int Minutes,
    [property: JsonPropertyName("sweaty")] bool IsSweaty,
    [property: JsonPropertyName("participants")] IReadOnlyList<string> Participants,
    [property: JsonPropertyName("together")] string Together,
    [property: JsonPropertyName("exercises")] IReadOnlyList<string> Exercises,
    [property: JsonPropertyName("scoreBpm")] int? ScoreBpm);

/// <summary>The planner's one-line reason for a change to a person's day.</summary>
public sealed record StoredReason(
    [property: JsonPropertyName("person")] string Person,
    [property: JsonPropertyName("day")] DateOnly Day,
    [property: JsonPropertyName("line")] string Line);

/// <summary>A number issued to a session (see <see cref="KeyIssue"/>), kept so the tab names events by the keys the run wrote.</summary>
public sealed record StoredIssue(
    [property: JsonPropertyName("person")] string Person,
    [property: JsonPropertyName("week")] int Week,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("weekday")] int Weekday,
    [property: JsonPropertyName("slot")] int SlotInDay,
    [property: JsonPropertyName("ordinal")] int Ordinal);

/// <summary>What Today clips for one person on the day of the run: no kind, start or minutes is a rest day.</summary>
public sealed record StoredToday(
    [property: JsonPropertyName("person")] string Person,
    [property: JsonPropertyName("day")] DateOnly Day,
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("start")] TimeOnly? Start,
    [property: JsonPropertyName("minutes")] int? Minutes,
    [property: JsonPropertyName("scoreBpm")] int? ScoreBpm);

/// <summary>What the last run that planned the week left for the Workouts tab and Today to read.</summary>
public sealed record StoredWorkoutWeek(
    [property: JsonPropertyName("weekStart")] DateOnly WeekStart,
    [property: JsonPropertyName("ranAt")] DateTimeOffset RanAt,
    [property: JsonPropertyName("sessions")] IReadOnlyList<StoredSession> Sessions,
    [property: JsonPropertyName("reasons")] IReadOnlyList<StoredReason> Reasons,
    [property: JsonPropertyName("assignments")] IReadOnlyList<StoredIssue> Assignments,
    [property: JsonPropertyName("today")] IReadOnlyList<StoredToday> Today);

/// <summary>
/// Makes the stored record from a run's report. Pure. The sessions are the PLACED plan (what went to the calendar); the reasons and
/// today's clipping read the adapted week, which is what the paper says. The headline Score setting is the one the page prints first
/// for the kind (steady for strength and the cardio kinds, easy for the rest, and easy whenever soreness eased the day).
/// </summary>
public static class WorkoutWeekRecordBuilder
{
    public static StoredWorkoutWeek From(WorkoutWeekReport report, KeyAssignments assignments, DateTimeOffset ranAt, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(assignments);

        var sessions = report.PlacedPlan.Plans
            .SelectMany(plan => plan.Sessions.Select(session => Session(plan.Person, session, EasedBy(report, plan.Person, session.Day))))
            .ToList();

        var reasons = new List<StoredReason>();
        foreach (var swap in report.RecoverySwaps)
        {
            reasons.AddRange(swap.People.Select(person => new StoredReason(person, swap.Day, swap.Reason)));
        }

        reasons.AddRange(report.SorenessAdjustments.Select(item => new StoredReason(item.Person, item.Day, item.Reason)));
        foreach (var change in report.ConditionChanges)
        {
            reasons.AddRange(change.Original.Participants.Select(person => new StoredReason(person, change.Original.Day, change.Line)));
        }

        foreach (var change in report.TravelChanges)
        {
            reasons.AddRange(change.Original.Participants.Select(person => new StoredReason(person, change.Original.Day, change.Line)));
        }

        reasons.AddRange(report.DayNotes.Select(note => new StoredReason(note.Person, note.Day, note.Line)));

        var clipping = report.Plan.Plans.Select(plan =>
        {
            var session = plan.SessionsOn(today).FirstOrDefault();
            return session is null
                ? new StoredToday(plan.Person, today, null, null, null, null)
                : new StoredToday(
                    plan.Person,
                    today,
                    WorkoutWireNames.Of(session.Session.Kind),
                    TimeOnly.FromDateTime(session.Start.DateTime),
                    (int)session.Session.Duration.TotalMinutes,
                    Headline(session.Session.Kind, EasedBy(report, plan.Person, today)));
        }).ToList();

        var issued = assignments.Issues
            .Select(issue => new StoredIssue(issue.Person, issue.Week, WorkoutWireNames.Of(issue.Kind), (int)issue.Weekday, issue.SlotInDay, issue.Ordinal))
            .ToList();

        return new StoredWorkoutWeek(report.PlacedPlan.WeekStart, ranAt, sessions, reasons, issued, clipping);
    }

    private static StoredSession Session(string person, PlannedSession planned, bool eased) =>
        new(
            person,
            planned.Day,
            planned.Start,
            WorkoutWireNames.Of(planned.Session.Kind),
            (int)planned.Session.Duration.TotalMinutes,
            planned.Session.IsSweaty,
            planned.Participants.ToList(),
            planned.Together.ToString(),
            planned.Session.Exercises.Select(exercise => exercise.Name).ToList(),
            Headline(planned.Session.Kind, eased));

    private static bool EasedBy(WorkoutWeekReport report, string person, DateOnly day) =>
        report.SorenessAdjustments.Any(item => item.EasyTempo && item.Day == day && string.Equals(item.Person, person, StringComparison.OrdinalIgnoreCase));

    private static int Headline(SessionKind kind, bool eased) =>
        TempoBands.Default(!eased && kind is SessionKind.Strength or SessionKind.OutdoorCardio or SessionKind.Treadmill ? TempoBand.Steady : TempoBand.Easy);
}

/// <summary>
/// The stored record as JSON, with a version. Pure. A text that is not this record is <see cref="WorkoutWeekRecordRead.Unreadable"/>
/// with a fixed sentence (nothing of the text is quoted), never an empty record: an empty week would read as "the plan has nothing in it".
/// Unknown fields are ignored so a later release can add one.
/// </summary>
public static class WorkoutWeekReportCodec
{
    public const int Version = 1;

    private const string VersionName = "version";

    private static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static string Encode(StoredWorkoutWeek week)
    {
        ArgumentNullException.ThrowIfNull(week);
        var node = JsonSerializer.SerializeToNode(week, Options)!.AsObject();
        var versioned = new JsonObject { [VersionName] = Version };
        foreach (var property in node.ToList())
        {
            node.Remove(property.Key);
            versioned[property.Key] = property.Value;
        }

        return versioned.ToJsonString();
    }

    public static WorkoutWeekRecordRead Decode(string? json)
    {
        if (json is null)
        {
            return new WorkoutWeekRecordRead.NothingYet();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Unreadable("The saved week is not a record.");
            }

            if (!document.RootElement.TryGetProperty(VersionName, out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != Version)
            {
                return Unreadable("The saved week is not a version this app reads.");
            }

            var week = document.RootElement.Deserialize<StoredWorkoutWeek>(Options);
            return IsWhole(week) ? new WorkoutWeekRecordRead.Held(week!) : Unreadable("The saved week is missing parts it needs.");
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or FormatException)
        {
            return Unreadable("The saved week could not be read.");
        }
    }

    private static WorkoutWeekRecordRead Unreadable(string reason) => new WorkoutWeekRecordRead.Unreadable(reason);

    private static bool IsWhole(StoredWorkoutWeek? week) =>
        week is not null
        && week.WeekStart.DayOfWeek == DayOfWeek.Monday
        && week.Sessions is not null && week.Sessions.All(item => item is { Person: not null, Kind: not null, Participants: not null, Together: not null, Exercises: not null })
        && week.Reasons is not null && week.Reasons.All(item => item is { Person: not null, Line: not null })
        && week.Assignments is not null && week.Assignments.All(item => item is { Person: not null, Kind: not null })
        && week.Today is not null && week.Today.All(item => item is { Person: not null });
}

/// <summary>What reading the stored record gave: none yet, the record, or a file that is there and cannot be read (never an empty record).</summary>
public abstract record WorkoutWeekRecordRead
{
    private WorkoutWeekRecordRead()
    {
    }

    public sealed record NothingYet : WorkoutWeekRecordRead;

    public sealed record Held(StoredWorkoutWeek Week) : WorkoutWeekRecordRead;

    public sealed record Unreadable(string Reason) : WorkoutWeekRecordRead;
}

/// <summary>Where the last run's report is kept for the tab and Today.</summary>
public interface IWorkoutWeekReports
{
    WorkoutWeekRecordRead Load();

    /// <summary>Keeps the week. A file already there that cannot be read is set aside first (<see cref="UnreadableCopies"/>), never written over.</summary>
    void Save(StoredWorkoutWeek week);
}

/// <summary>The report in one small file in the app's private storage, read and replaced whole (atomically).</summary>
public sealed class FileWorkoutWeekReports : IWorkoutWeekReports
{
    public const string UnopenableReason = "The saved week could not be opened.";

    private static readonly object FileLock = new();

    private readonly string path;

    public FileWorkoutWeekReports(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public WorkoutWeekRecordRead Load()
    {
        lock (FileLock)
        {
            return Read();
        }
    }

    public void Save(StoredWorkoutWeek week)
    {
        ArgumentNullException.ThrowIfNull(week);
        lock (FileLock)
        {
            if (Read() is WorkoutWeekRecordRead.Unreadable)
            {
                UnreadableCopies.SetAside(path);
            }

            AtomicFile.WriteAllText(path, WorkoutWeekReportCodec.Encode(week));
        }
    }

    private WorkoutWeekRecordRead Read()
    {
        if (!File.Exists(path))
        {
            return new WorkoutWeekRecordRead.NothingYet();
        }

        try
        {
            return WorkoutWeekReportCodec.Decode(File.ReadAllText(path));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The sentence is the reason; the exception can quote a path and the type is the caller's to log.
            return new WorkoutWeekRecordRead.Unreadable(UnopenableReason);
        }
    }
}
