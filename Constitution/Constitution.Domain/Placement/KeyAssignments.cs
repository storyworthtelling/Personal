using System.Text;
using System.Text.Json;
using Constitution.Domain.Workouts;

namespace Constitution.Domain.Placement;

/// <summary>
/// A number issued to one session of one person's week. <b>The session is named by where it sits in the calendar week, not
/// by where it sits among its kind</b>: its kind, the weekday it is on and, for a second session of the same kind on the same
/// day, which of them it is (<see cref="SlotInDay"/>). That is what survives a re-plan: the week is planned again from its
/// first instant on every run, and the owner adding a Monday meeting takes Monday's strength session out of the plan without
/// making Wednesday's any less Wednesday's.
///
/// <para>The <see cref="Ordinal"/> is the number in the calendar key (<see cref="PlacementKey.ForWorkout"/>), so the key format
/// is unchanged and still carries no date. The person is kept trimmed and lower case, as the key keeps it.</para>
/// </summary>
public sealed record KeyIssue
{
    public KeyIssue(string person, int week, SessionKind kind, DayOfWeek weekday, int slotInDay, int ordinal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(person);
        ArgumentOutOfRangeException.ThrowIfNegative(week);
        ArgumentOutOfRangeException.ThrowIfNegative(slotInDay);
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "That is not a kind of session.");
        }

        if (!Enum.IsDefined(weekday))
        {
            throw new ArgumentOutOfRangeException(nameof(weekday), weekday, "That is not a day of the week.");
        }

        Person = person.Trim().ToLowerInvariant();
        Week = week;
        Kind = kind;
        Weekday = weekday;
        SlotInDay = slotInDay;
        Ordinal = ordinal;
    }

    public string Person { get; }

    public int Week { get; }

    public SessionKind Kind { get; }

    public DayOfWeek Weekday { get; }

    public int SlotInDay { get; }

    public int Ordinal { get; }

    /// <summary>The key this number makes.</summary>
    public PlacementKey Key => PlacementKey.ForWorkout(Person, Kind, Week, Ordinal);
}

/// <summary>
/// The durable record of the workout numbers that have been issued (B2). Without it a key is "the nth session of its kind this
/// run", and because the week is re-planned from its start on every run, one session dropped from the front renumbers every
/// session after it: the events already on the calendar are then held under keys that now name other days, nothing new is
/// written, and the pages for those days are taken from events that sit on the wrong day.
///
/// <para>A number is issued to a session identity (<see cref="KeyIssue"/>) once and given back to that identity on every later
/// run. A new session takes the number after the highest <b>ever</b> issued for its person, week and kind, never a number a
/// dropped session has let go of, so the key of an old event can never come to name a different session. The record is only
/// ever grown, as the record of written keys is, and what it holds is names: nobody's time, load or page.</para>
/// </summary>
public sealed class KeyAssignments
{
    private readonly Dictionary<(string Person, int Week, SessionKind Kind, DayOfWeek Weekday, int Slot), KeyIssue> byIdentity;
    private readonly Dictionary<(string Person, int Week, SessionKind Kind, int Ordinal), KeyIssue> byNumber;
    private readonly Dictionary<(string Person, int Week, SessionKind Kind), int> highest;
    private readonly HashSet<PlacementKey> keys;

    private KeyAssignments(IEnumerable<KeyIssue> issues)
    {
        byIdentity = [];
        byNumber = [];
        highest = [];
        keys = [];
        foreach (var issue in issues)
        {
            byIdentity[(issue.Person, issue.Week, issue.Kind, issue.Weekday, issue.SlotInDay)] = issue;
            byNumber[(issue.Person, issue.Week, issue.Kind, issue.Ordinal)] = issue;
            var group = (issue.Person, issue.Week, issue.Kind);
            highest[group] = highest.TryGetValue(group, out var peak) ? Math.Max(peak, issue.Ordinal) : issue.Ordinal;
            keys.Add(issue.Key);
        }

        Issues = byIdentity.Values
            .OrderBy(issue => issue.Person, StringComparer.Ordinal)
            .ThenBy(issue => issue.Week)
            .ThenBy(issue => PlacementKey.NameOfKind(issue.Kind), StringComparer.Ordinal)
            .ThenBy(issue => issue.Ordinal)
            .ToList()
            .AsReadOnly();
    }

    public static KeyAssignments Empty { get; } = new([]);

    /// <summary>Every issue, once each, in a fixed order.</summary>
    public IReadOnlyList<KeyIssue> Issues { get; }

    /// <summary>The number issued to that session identity, or null when none has been.</summary>
    public KeyIssue? Find(string person, int week, SessionKind kind, DayOfWeek weekday, int slotInDay) =>
        byIdentity.GetValueOrDefault((Normal(person), week, kind, weekday, slotInDay));

    /// <summary>The highest number ever issued to this person's sessions of this kind in this week, or null when none has.</summary>
    public int? HighestOrdinal(string person, int week, SessionKind kind) =>
        highest.TryGetValue((Normal(person), week, kind), out var peak) ? peak : null;

    /// <summary>Whether this record is why the key names the session it does: some session was issued exactly this number.</summary>
    public bool Vouches(PlacementKey key) => keys.Contains(key);

    /// <summary>This record with more issues. The record it is called on is unchanged.</summary>
    /// <exception cref="ArgumentException">A session identity would hold two numbers, or a number would name two sessions.</exception>
    public KeyAssignments With(IEnumerable<KeyIssue> more)
    {
        ArgumentNullException.ThrowIfNull(more);
        var added = new List<KeyIssue>();
        foreach (var issue in more)
        {
            var identity = (issue.Person, issue.Week, issue.Kind, issue.Weekday, issue.SlotInDay);
            var held = byIdentity.GetValueOrDefault(identity) ?? added.FirstOrDefault(item => Same(item, issue));
            if (held is not null)
            {
                if (held.Ordinal == issue.Ordinal)
                {
                    continue;
                }

                throw new ArgumentException($"That session was already issued number {held.Ordinal}; it cannot be given {issue.Ordinal} as well.", nameof(more));
            }

            var number = (issue.Person, issue.Week, issue.Kind, issue.Ordinal);
            if (byNumber.ContainsKey(number) || added.Any(item => item.Person == issue.Person && item.Week == issue.Week && item.Kind == issue.Kind && item.Ordinal == issue.Ordinal))
            {
                throw new ArgumentException($"Number {issue.Ordinal} was already issued to another session; a number names one session only.", nameof(more));
            }

            added.Add(issue);
        }

        return added.Count == 0 ? this : new KeyAssignments(Issues.Concat(added));
    }

    private static bool Same(KeyIssue left, KeyIssue right) =>
        left.Person == right.Person && left.Week == right.Week && left.Kind == right.Kind && left.Weekday == right.Weekday && left.SlotInDay == right.SlotInDay;

    private static string Normal(string person) => person.Trim().ToLowerInvariant();
}

/// <summary>
/// What reading the record gave: the issues, or the reason it could not be read. The two are never confused: a record that
/// cannot be read is <b>not</b> an empty one, because read as empty every session would be handed a fresh number and the
/// events already on the calendar would be held under keys that name other days.
/// </summary>
public sealed class KeyAssignmentsRead
{
    private KeyAssignmentsRead(KeyAssignments? assignments, string? unreadableBecause)
    {
        Assignments = assignments ?? KeyAssignments.Empty;
        UnreadableBecause = unreadableBecause;
    }

    /// <summary>The issues; empty when the record is unreadable.</summary>
    public KeyAssignments Assignments { get; }

    /// <summary>Why the record could not be read, or null when it could.</summary>
    public string? UnreadableBecause { get; }

    public bool IsReadable => UnreadableBecause is null;

    public static KeyAssignmentsRead Of(KeyAssignments assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        return new KeyAssignmentsRead(assignments, null);
    }

    public static KeyAssignmentsRead Unreadable(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An unreadable record has to say why it could not be read.", nameof(reason));
        }

        return new KeyAssignmentsRead(null, reason.Trim());
    }
}

/// <summary>
/// The record as text: <c>{"version":1,"issued":[{"person":"sam","week":105703,"kind":"strength","weekday":"mon","slot":0,"ordinal":0}, ...]}</c>,
/// sorted so the same record is the same text. The kind is spelled as the calendar key spells it and the weekday as a three-letter
/// literal, never an enum's name, because renaming a member must not rename what is stored. Nothing stored yet (null or blank)
/// is an empty record; anything else that cannot be read, a version this code does not know included, is unreadable.
/// </summary>
public static class KeyAssignmentsCodec
{
    private const int CurrentVersion = 1;

    public static string Encode(KeyAssignments assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", CurrentVersion);
            writer.WriteStartArray("issued");
            foreach (var issue in assignments.Issues)
            {
                writer.WriteStartObject();
                writer.WriteString("person", issue.Person);
                writer.WriteNumber("week", issue.Week);
                writer.WriteString("kind", PlacementKey.NameOfKind(issue.Kind));
                writer.WriteString("weekday", WeekdayName(issue.Weekday));
                writer.WriteNumber("slot", issue.SlotInDay);
                writer.WriteNumber("ordinal", issue.Ordinal);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static KeyAssignmentsRead Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return KeyAssignmentsRead.Of(KeyAssignments.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return Read(document.RootElement);
        }
        catch (JsonException error)
        {
            return KeyAssignmentsRead.Unreadable($"The record of issued keys is not valid JSON ({error.Message}).");
        }
    }

    private static KeyAssignmentsRead Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return KeyAssignmentsRead.Unreadable("The record of issued keys is not a JSON object.");
        }

        if (!root.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var number)
            || number != CurrentVersion)
        {
            return KeyAssignmentsRead.Unreadable($"The record of issued keys is not version {CurrentVersion}, so it was not read.");
        }

        if (!root.TryGetProperty("issued", out var issued) || issued.ValueKind != JsonValueKind.Array)
        {
            return KeyAssignmentsRead.Unreadable("The record of issued keys has no list of issues.");
        }

        var issues = new List<KeyIssue>();
        foreach (var item in issued.EnumerateArray())
        {
            if (!TryRead(item, out var issue))
            {
                return KeyAssignmentsRead.Unreadable("The record of issued keys holds an entry that is not an issue this app wrote.");
            }

            issues.Add(issue!);
        }

        try
        {
            return KeyAssignmentsRead.Of(KeyAssignments.Empty.With(issues));
        }
        catch (ArgumentException conflict)
        {
            return KeyAssignmentsRead.Unreadable($"The record of issued keys contradicts itself ({conflict.Message}).");
        }
    }

    private static bool TryRead(JsonElement item, out KeyIssue? issue)
    {
        issue = null;
        if (item.ValueKind != JsonValueKind.Object
            || !TryText(item, "person", out var person)
            || !TryNumber(item, "week", out var week)
            || !TryText(item, "kind", out var kindName)
            || !PlacementKey.TryKindNamed(kindName, out var kind)
            || !TryText(item, "weekday", out var weekdayName)
            || !TryWeekday(weekdayName, out var weekday)
            || !TryNumber(item, "slot", out var slot)
            || !TryNumber(item, "ordinal", out var ordinal)
            || string.IsNullOrWhiteSpace(person)
            || week < 0 || slot < 0 || ordinal < 0)
        {
            return false;
        }

        issue = new KeyIssue(person!, week, kind, weekday, slot, ordinal);
        return true;
    }

    private static bool TryText(JsonElement item, string name, out string? text)
    {
        text = null;
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        text = value.GetString();
        return text is not null;
    }

    private static bool TryNumber(JsonElement item, string name, out int number)
    {
        number = 0;
        return item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number);
    }

    private static string WeekdayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => "sun",
        DayOfWeek.Monday => "mon",
        DayOfWeek.Tuesday => "tue",
        DayOfWeek.Wednesday => "wed",
        DayOfWeek.Thursday => "thu",
        DayOfWeek.Friday => "fri",
        DayOfWeek.Saturday => "sat",
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, "That is not a day of the week."),
    };

    private static bool TryWeekday(string? name, out DayOfWeek day)
    {
        foreach (var candidate in Enum.GetValues<DayOfWeek>())
        {
            if (string.Equals(WeekdayName(candidate), name, StringComparison.Ordinal))
            {
                day = candidate;
                return true;
            }
        }

        day = default;
        return false;
    }
}

/// <summary>
/// Where the record is kept between runs. <see cref="Load"/> says plainly when the record cannot be read; <see cref="Save"/> is
/// never called with a record built on top of an unreadable one, so a damaged record is left for a person to look at.
/// </summary>
public interface IKeyAssignmentStore
{
    KeyAssignmentsRead Load();

    void Save(KeyAssignments assignments);
}
