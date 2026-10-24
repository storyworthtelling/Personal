using System.Globalization;
using Constitution.Domain.Calendar;
using Constitution.Domain.Workouts;

namespace Constitution.Domain.Placement;

/// <summary>An event placement was asked to consider, before any time is chosen for it.</summary>
public sealed record WantedEvent(PlacementKey Key, string Person, string Title);

/// <summary>
/// What the calendars and the record say about the events being placed. Built by the runner from what it found.
/// </summary>
/// <param name="Written">Keys known to have been written (see <see cref="WrittenKeys"/>).</param>
/// <param name="Held">Our events found on the calendars, wherever they now are.</param>
/// <param name="UnavailablePeople">People whose calendar could not be looked into: nothing is concluded about them.</param>
public sealed record CalendarState(WrittenKeys Written, IReadOnlyList<HeldEvent> Held, IReadOnlySet<string> UnavailablePeople);

/// <summary>
/// Calendar placement (C-5.3.2) and the owner's edits (C-5.3.3): the pure half. Given the week's busy time, the
/// household events, the workout plan and what the calendars hold, it decides what becomes of every event.
///
/// <para><b>What becomes of an event</b> is decided first, by its key and nothing else. If the calendar holds
/// it, that is <see cref="EventHeld"/> at the calendar's time, and the time stands whether the owner moved it or
/// not: placement never compares, never moves, never writes. If it is not on the calendar but was once written,
/// the owner deleted it: <see cref="EventSkipped"/>, for good, with no question and no notice. Only an event that
/// was never written is placed.</para>
///
/// <para><b>How new events are placed.</b> Events already on the calendar claim their time first (a sweaty
/// workout's shower included). Then household events in the order given, then workouts, so meals and shopping win
/// any contest for a minute and workouts go around them. A household event takes the earliest minute inside the
/// first of its windows where every person it is for is free. A workout stays inside its
/// <see cref="PlannedSession.Gap"/>, takes the planner's proposed start if that fits and otherwise the earliest
/// minute that does, and a sweaty one also needs the shower buffer free after it, inside the gap. Free means free
/// of busy time, never-placed time and anything already placed; touching is not overlapping.</para>
///
/// <para><b>Fail closed.</b> An event that does not fit is <see cref="EventUnplaced"/> with the reason, never
/// squeezed in, shortened or placed on a guess. A person whose calendar could not be read or looked into has
/// nothing placed for them, and a session shared with them waits as a whole.</para>
///
/// <para><b>Whose key a session wears is decided by what was issued, not by where it sits this run</b> (B2,
/// <see cref="SessionKeys"/>). The week is re-planned from its first instant on every run, and a number that means "the nth
/// strength session" moves the moment one session is dropped from the front: Wednesday's event is then held under the key that
/// now names Friday's session, nothing new is written, and Friday's event is left under a key nobody wants. With a record of
/// issued numbers (<see cref="PlacementRequest.Assignments"/>) a session keeps its key however the plan around it changes, and a
/// new session takes a number above every one ever issued.</para>
///
/// <para><b>And where the record cannot vouch for a key the engine does not guess.</b> A key it has just numbered (the record has
/// no issue for it) that already stands on the calendar on another day than its session, or is recorded as written with nothing
/// on the calendar, or a workout key of the week that nobody issued: any of these means the numbering the events were written
/// under is not this one, so that person's sessions of that kind are left undecided (<see cref="UnplacedReason.KeysUncertain"/>,
/// <see cref="PlacementPlan.UncertainKinds"/>): not written, not read as deleted, not paged. A key the record does vouch for, on
/// another day, is the owner's move, and stands.</para>
/// </summary>
public static class PlacementEngine
{
    /// <summary>The events the request asks for, with their keys and titles, before any time is chosen.</summary>
    /// <exception cref="ArgumentException">The request is not coherent (an unknown person, a repeated identity, a title that holds its key).</exception>
    public static IReadOnlyList<WantedEvent> Wanted(PlacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Build(request).Units
            .SelectMany(unit => unit.Copies.Select(copy => new WantedEvent(copy.Key, copy.Person, unit.Title)))
            .ToList();
    }

    /// <summary>Decides what becomes of every event in the request. Pure: the same inputs always give the same plan.</summary>
    /// <exception cref="ArgumentException">The request is not coherent.</exception>
    public static PlacementPlan Plan(PlacementRequest request, CalendarState state)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(state);

        var layout = Build(request);
        var units = layout.Units;
        var ledger = new PlacementLedger(request.People);
        var held = state.Held
            .GroupBy(item => item.Key)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.EventId, StringComparer.Ordinal).First());

        // First pass: what the calendars already say. The owner's times are claimed before anything new is placed.
        var standings = new Dictionary<PlacementKey, Standing>();
        var doubts = new Dictionary<(string Person, SessionKind Kind), string>();
        foreach (var unit in units)
        {
            foreach (var copy in unit.Copies)
            {
                if (state.UnavailablePeople.Contains(copy.Person))
                {
                    standings[copy.Key] = Standing.Unavailable;
                }
                else if (held.TryGetValue(copy.Key, out var found))
                {
                    standings[copy.Key] = Standing.Held;
                    ledger.Claim(copy.Person, found.Start, found.End + unit.Shower);
                    if (copy.Kind is { } kind && !copy.Vouched && !SameDay(found, copy))
                    {
                        doubts.TryAdd((Normal(copy.Person), kind), "An event under a key just numbered for this session stands on another day, and no record says the key was ever issued to it.");
                    }
                }
                else if (state.Written.Contains(copy.Key))
                {
                    standings[copy.Key] = Standing.Skipped;
                    if (copy.Kind is { } kind && !copy.Vouched)
                    {
                        doubts.TryAdd((Normal(copy.Person), kind), "A key just numbered for this session is recorded as written with nothing on the calendar, and no record says it was ever issued to it: a delete cannot be told from a different numbering.");
                    }
                }
                else
                {
                    standings[copy.Key] = Standing.Fresh;
                }
            }
        }

        // An event under a workout key of this week that nobody asked for and no record issued is a key from some other
        // numbering. It only matters to the person and kind whose sessions are being placed.
        if (layout.Record is { } record)
        {
            var asked = units.SelectMany(unit => unit.Copies).Select(copy => copy.Key).ToHashSet();
            foreach (var stray in held.Values.Where(item => !asked.Contains(item.Key) && !record.Vouches(item.Key)))
            {
                if (PlacementKey.TryWorkoutParts(stray.Key, out var owner, out var kind, out var week, out _)
                    && week == layout.Week
                    && units.Any(unit => unit.Copies.Any(copy => copy.Kind == kind && Normal(copy.Person) == Normal(owner))))
                {
                    doubts.TryAdd((Normal(owner), kind), "An event holds a workout key of this week that no record issued.");
                }
            }
        }

        foreach (var unit in units)
        {
            foreach (var copy in unit.Copies)
            {
                if (copy.Kind is { } kind && doubts.ContainsKey((Normal(copy.Person), kind)) && standings[copy.Key] != Standing.Unavailable)
                {
                    standings[copy.Key] = Standing.Uncertain;
                }
            }
        }

        // Second pass: place what is new, household events first (Build puts them first).
        var outcomes = new List<PlacementOutcome>();
        var people = request.People.ToDictionary(person => person.Person, StringComparer.OrdinalIgnoreCase);
        foreach (var unit in units)
        {
            var fresh = unit.Copies.Where(copy => standings[copy.Key] == Standing.Fresh).ToList();
            var decision = fresh.Count == 0 ? null : Decide(unit, fresh, standings, people, ledger);
            foreach (var copy in unit.Copies)
            {
                outcomes.Add(standings[copy.Key] switch
                {
                    Standing.Held => new EventHeld(copy.Key, copy.Person, unit.Title, held[copy.Key].Start, held[copy.Key].End),
                    Standing.Skipped => new EventSkipped(copy.Key, copy.Person, unit.Title),
                    Standing.Unavailable => new EventUnplaced(copy.Key, copy.Person, unit.Title, UnplacedReason.CalendarUnavailable),
                    Standing.Uncertain => new EventUnplaced(copy.Key, copy.Person, unit.Title, UnplacedReason.KeysUncertain),
                    _ => decision!.Outcome(copy, unit),
                });
            }
        }

        // What to keep: the numbers for sessions the record did not know, unless their person and kind are in doubt or their
        // calendar was not looked into (a number kept without having seen the calendar would later vouch for an event it never met).
        var issued = layout.Keys?.Issued
            .Where(issue => layout.Record is not null
                && !doubts.ContainsKey((issue.Person, issue.Kind))
                && !state.UnavailablePeople.Any(name => Normal(name) == issue.Person))
            .ToList() ?? [];
        return new PlacementPlan(outcomes)
        {
            Issued = issued,
            UncertainKinds = doubts.Select(pair => new UncertainKind(pair.Key.Person, pair.Key.Kind, pair.Value)).ToList(),
        };
    }

    // The day the calendar has the event on, read in the offset the session was planned in.
    private static bool SameDay(HeldEvent found, Copy copy) =>
        copy.Day is { } day && DateOnly.FromDateTime(found.Start.ToOffset(copy.PlannedOffset).DateTime) == day;

    private static string Normal(string person) => person.Trim().ToLowerInvariant();

    // ---- placing one unit ----

    private static Decision Decide(
        Unit unit,
        List<Copy> fresh,
        Dictionary<PlacementKey, Standing> standings,
        Dictionary<string, BusyTime> people,
        PlacementLedger ledger)
    {
        // A session shared with someone whose calendar cannot be looked into waits as a whole.
        if (unit.Copies.Any(copy => standings[copy.Key] == Standing.Unavailable))
        {
            return Decision.Refused(UnplacedReason.CalendarUnavailable);
        }

        // So does one shared with someone whose numbering is in doubt: half of a shared session is a guess about the other half.
        if (unit.Copies.Any(copy => standings[copy.Key] == Standing.Uncertain))
        {
            return Decision.Refused(UnplacedReason.KeysUncertain);
        }

        var names = fresh.Select(copy => copy.Person).ToList();
        if (names.Any(name => !people[name].IsComplete))
        {
            return Decision.Refused(UnplacedReason.CalendarUnreadable);
        }

        var start = FindStart(unit, names, ledger, unit.Length + unit.Shower);
        if (start is null)
        {
            // Say which: the session would have fitted without its shower, or it would not have fitted at all.
            var without = unit.Shower > TimeSpan.Zero ? FindStart(unit, names, ledger, unit.Length) : null;
            return Decision.Refused(without is null ? UnplacedReason.NoRoom : UnplacedReason.NoRoomForShowerBuffer);
        }

        foreach (var name in names)
        {
            ledger.Claim(name, start.Value, start.Value + unit.Length + unit.Shower);
        }

        return Decision.Placed(start.Value, start.Value + unit.Length);
    }

    // The earliest minute with `needed` free for everyone, inside the first region that has room; a proposal that
    // fits wins over an earlier minute, so a planner that already chose well is not second-guessed.
    private static DateTimeOffset? FindStart(Unit unit, IReadOnlyList<string> names, PlacementLedger ledger, TimeSpan needed)
    {
        foreach (var region in unit.Regions)
        {
            var fits = ledger.CommonFree(names, region).Where(free => free.Length >= needed).ToList();
            if (fits.Count == 0)
            {
                continue;
            }

            if (unit.Proposal is { } proposal && fits.Any(free => free.Start <= proposal && proposal + needed <= free.End))
            {
                return proposal;
            }

            return fits[0].Start;
        }

        return null;
    }

    // ---- what is being asked for ----

    private static Layout Build(PlacementRequest request)
    {
        Validate(request);
        var week = PlacementKey.WeekIndexOf(request.WeekStart);
        var canonical = request.People.ToDictionary(person => person.Person, person => person.Person, StringComparer.OrdinalIgnoreCase);
        var units = new List<Unit>();

        foreach (var household in request.HouseholdEvents)
        {
            var copies = household.People
                .Select(name => canonical[name])
                .Select(person => new Copy(PlacementKey.ForHousehold(person, week, household.Identity), person))
                .OrderBy(copy => copy.Key.Value, StringComparer.Ordinal)
                .ToList();
            var regions = household.Windows.Select(window => new Stretch(window.Start, window.End)).ToList();
            units.Add(new Unit(household.Title, household.Length, TimeSpan.Zero, copies, regions, null));
        }

        SessionKeys? keys = null;
        if (request.Workouts is { } workouts)
        {
            keys = SessionKeys.For(workouts, request.Assignments);
            units.AddRange(BuildWorkouts(request, workouts, keys, canonical));
        }

        foreach (var unit in units)
        {
            foreach (var copy in unit.Copies)
            {
                CheckTitle(unit.Title, copy.Key);
            }
        }

        return new Layout(units, keys, request.Assignments, week);
    }

    private static List<Unit> BuildWorkouts(PlacementRequest request, HouseholdWorkoutPlan workouts, SessionKeys keys, Dictionary<string, string> canonical)
    {
        var sessions = new Dictionary<string, PlannedSession>(StringComparer.Ordinal);
        foreach (var session in workouts.Plans.SelectMany(plan => plan.Sessions).Concat(workouts.JointSessions))
        {
            sessions.TryAdd(SessionKeys.SessionId(session), session);
        }

        var units = new List<Unit>();
        foreach (var session in sessions.Values)
        {
            var copies = session.Participants
                .Select(name => canonical[name.Trim()])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(person =>
                {
                    var key = keys.KeyOf(person, session)!.Value;
                    return new Copy(key, person, session.Session.Kind, session.Day, session.Start.Offset, request.Assignments is null || request.Assignments.Vouches(key));
                })
                .OrderBy(copy => copy.Key.Value, StringComparer.Ordinal)
                .ToList();
            var shower = session.Session.IsSweaty ? request.Options.ShowerBuffer : TimeSpan.Zero;
            units.Add(new Unit(
                PlacementTitles.Workout(session.Session.Duration),
                session.Session.Duration,
                shower,
                copies,
                [new Stretch(session.Gap.Start, session.Gap.End)],
                session.Start));
        }

        return units.OrderBy(unit => unit.Proposal).ThenBy(unit => unit.Copies[0].Key.Value, StringComparer.Ordinal).ToList();
    }

    private static void Validate(PlacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.People);
        ArgumentNullException.ThrowIfNull(request.HouseholdEvents);
        if (request.WeekStart.DayOfWeek != DayOfWeek.Monday)
        {
            throw new ArgumentException(
                $"A placed week begins on a Monday, and {request.WeekStart:yyyy-MM-dd} is a {request.WeekStart.DayOfWeek}. A key names a session by its weekday within the week, so a week begun on another day would number the same days differently from one run to the next.",
                nameof(request));
        }

        var twice = request.People
            .GroupBy(person => person.Person, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (twice is not null)
        {
            throw new ArgumentException($"{twice.Key} appears twice in the request.", nameof(request));
        }

        var known = request.People.Select(person => person.Person).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var household in request.HouseholdEvents)
        {
            foreach (var person in household.People)
            {
                if (!known.Contains(person))
                {
                    throw new ArgumentException($"'{household.Title}' is for {person}, who has no busy time in the request.", nameof(request));
                }

                if (!taken.Add($"{person}\n{household.Identity.ToLowerInvariant()}"))
                {
                    throw new ArgumentException($"The identity '{household.Identity}' names two events for {person}; a key names one.", nameof(request));
                }
            }
        }

        if (request.Workouts is not { } workouts)
        {
            return;
        }

        if (workouts.WeekStart.DayOfWeek != DayOfWeek.Monday)
        {
            throw new ArgumentException($"The workout plan's week begins on a Monday, and {workouts.WeekStart:yyyy-MM-dd} is a {workouts.WeekStart.DayOfWeek}.", nameof(request));
        }

        if (PlacementKey.WeekIndexOf(workouts.WeekStart) != PlacementKey.WeekIndexOf(request.WeekStart))
        {
            throw new ArgumentException("The workout plan is for a different week than the one being placed.", nameof(request));
        }

        var unknown = workouts.Plans.SelectMany(plan => plan.Sessions).Concat(workouts.JointSessions)
            .SelectMany(session => session.Participants)
            .Select(name => name.Trim())
            .FirstOrDefault(name => !known.Contains(name));
        if (unknown is not null)
        {
            throw new ArgumentException($"A workout is planned for {unknown}, who has no busy time in the request.", nameof(request));
        }
    }

    private static void CheckTitle(string title, PlacementKey key)
    {
        if (title.Contains(key.Value, StringComparison.OrdinalIgnoreCase)
            || title.Contains(PlacementKey.Property, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The title '{title}' holds its own key. A title is clean; the key lives in a hidden property.", nameof(title));
        }
    }

    private enum Standing
    {
        Fresh,
        Held,
        Skipped,
        Unavailable,
        Uncertain,
    }

    // A workout copy also says what it is (kind, day, the offset it was planned in) and whether the record of issued numbers
    // vouches for its key; a household copy has no kind and is never in doubt.
    private sealed record Copy(PlacementKey Key, string Person, SessionKind? Kind = null, DateOnly? Day = null, TimeSpan PlannedOffset = default, bool Vouched = true);

    private sealed record Layout(List<Unit> Units, SessionKeys? Keys, KeyAssignments? Record, int Week);

    private sealed record Unit(string Title, TimeSpan Length, TimeSpan Shower, List<Copy> Copies, List<Stretch> Regions, DateTimeOffset? Proposal);

    private sealed class Decision
    {
        private readonly UnplacedReason? reason;
        private readonly DateTimeOffset start;
        private readonly DateTimeOffset end;

        private Decision(UnplacedReason? reason, DateTimeOffset start, DateTimeOffset end)
        {
            this.reason = reason;
            this.start = start;
            this.end = end;
        }

        public static Decision Refused(UnplacedReason reason) => new(reason, default, default);

        public static Decision Placed(DateTimeOffset start, DateTimeOffset end) => new(null, start, end);

        public PlacementOutcome Outcome(Copy copy, Unit unit) => reason is { } why
            ? new EventUnplaced(copy.Key, copy.Person, unit.Title, why)
            : new EventToCreate(copy.Key, copy.Person, unit.Title, start, end);
    }
}

/// <summary>The words on the calendar. A workout is titled by what it is and how long, and by nothing else.</summary>
internal static class PlacementTitles
{
    public static string Workout(TimeSpan length) =>
        string.Create(CultureInfo.InvariantCulture, $"Workout · {(int)Math.Round(length.TotalMinutes)} min");
}
