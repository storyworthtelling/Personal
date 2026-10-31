using System.Globalization;
using Constitution.Domain.Workouts;

namespace Constitution.Domain.Placement;

/// <summary>
/// The key every session of a week wears (B2), worked out from what has been issued (<see cref="KeyAssignments"/>) and from
/// nothing else: the same plan and the same record always give the same keys, and the one thing a plan cannot change is the key
/// of a session that already has one. A session that has none takes the number after the highest ever issued to its person's
/// sessions of that kind that week, in time order, so with an empty record the numbers are the old ones (the nth of its kind).
///
/// <para>This is the one place the numbering is decided. <see cref="PlacementEngine"/> places by it, the weekly run's
/// <c>PlacementIndex</c> finds events by it and the Workouts tab's <c>WorkoutKeys</c> names events by it, so a page, a swap and
/// a skip all mean the event the calendar holds.</para>
/// </summary>
public sealed class SessionKeys
{
    private readonly Dictionary<(string Person, string Session), KeyIssue> issues;
    private readonly Dictionary<(string Person, DateOnly Day), string> firstOnDay;

    private SessionKeys(
        Dictionary<(string Person, string Session), KeyIssue> issues,
        Dictionary<(string Person, DateOnly Day), string> firstOnDay,
        IReadOnlyList<KeyIssue> issued,
        KeyAssignments assignments)
    {
        this.issues = issues;
        this.firstOnDay = firstOnDay;
        Issued = issued;
        Assignments = assignments;
    }

    /// <summary>The record that was given, with what this resolution issued added to it.</summary>
    public KeyAssignments Assignments { get; }

    /// <summary>The numbers this resolution issued: sessions the record did not know. Empty when the record knew them all.</summary>
    public IReadOnlyList<KeyIssue> Issued { get; }

    /// <param name="plan">The week's plan, from its start.</param>
    /// <param name="assignments">What has been issued so far; null is the same as nothing.</param>
    public static SessionKeys For(HouseholdWorkoutPlan plan, KeyAssignments? assignments)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var prior = assignments ?? KeyAssignments.Empty;
        var week = PlacementKey.WeekIndexOf(plan.WeekStart);

        // The same session reached by two routes (each participant's plan, and the joint list) is one session.
        var sessions = new Dictionary<string, PlannedSession>(StringComparer.Ordinal);
        foreach (var session in plan.Plans.SelectMany(weekly => weekly.Sessions).Concat(plan.JointSessions))
        {
            sessions.TryAdd(SessionId(session), session);
        }

        var issuedTo = new Dictionary<(string Person, string Session), KeyIssue>();
        var first = new Dictionary<(string Person, DateOnly Day), string>();
        var issued = new List<KeyIssue>();
        var people = sessions.Values
            .SelectMany(session => session.Participants)
            .Select(Normal)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        foreach (var person in people)
        {
            var mine = sessions
                .Where(pair => pair.Value.Participants.Any(name => Normal(name) == person))
                .OrderBy(pair => pair.Value.Start)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .ToList();

            foreach (var pair in mine)
            {
                first.TryAdd((person, pair.Value.Day), pair.Key);
            }

            foreach (var ofKind in mine.GroupBy(pair => pair.Value.Session.Kind).OrderBy(group => group.Key))
            {
                var highest = prior.HighestOrdinal(person, week, ofKind.Key) ?? -1;
                var onTheDay = new Dictionary<DayOfWeek, int>();
                foreach (var pair in ofKind)
                {
                    var weekday = pair.Value.Day.DayOfWeek;
                    var slot = onTheDay.GetValueOrDefault(weekday);
                    onTheDay[weekday] = slot + 1;
                    var issue = prior.Find(person, week, ofKind.Key, weekday, slot);
                    if (issue is null)
                    {
                        highest++;
                        issue = new KeyIssue(person, week, ofKind.Key, weekday, slot, highest);
                        issued.Add(issue);
                    }

                    issuedTo[(person, pair.Key)] = issue;
                }
            }
        }

        return new SessionKeys(issuedTo, first, issued, prior.With(issued));
    }

    /// <summary>The number issued to this person's session, or null when they have no such session in the plan.</summary>
    public KeyIssue? IssueOf(string person, PlannedSession session)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(session);
        return issues.GetValueOrDefault((Normal(person), SessionId(session)));
    }

    /// <summary>The key this person's session wears, or null when they have no such session in the plan.</summary>
    public PlacementKey? KeyOf(string person, PlannedSession session) => IssueOf(person, session)?.Key;

    /// <summary>The key of the person's session on that day (the earlier, if they have two), or null when they have none.</summary>
    public PlacementKey? KeyOn(string person, DateOnly day) => IssueOn(person, day)?.Key;

    /// <summary>The number issued to the person's session on that day (the earlier, if they have two), or null when they have none.</summary>
    public KeyIssue? IssueOn(string person, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(person);
        var name = Normal(person);
        return firstOnDay.TryGetValue((name, day), out var session) ? issues[(name, session)] : null;
    }

    /// <summary>The identity of a session as a string: every field that makes it this session and no other.</summary>
    internal static string SessionId(PlannedSession session) => string.Join(
        '|',
        session.Start.ToString("O", CultureInfo.InvariantCulture),
        session.Gap.Start.ToString("O", CultureInfo.InvariantCulture),
        session.Gap.End.ToString("O", CultureInfo.InvariantCulture),
        (int)session.Session.Kind,
        session.Session.Duration.Ticks,
        (int)session.Together,
        string.Join(',', session.Participants.Select(name => Normal(name)).Order(StringComparer.Ordinal)));

    private static string Normal(string person) => person.Trim().ToLowerInvariant();
}
