using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;

namespace Constitution.Domain.Workouts;

/// <summary>
/// One week's planning, start to finish. Joint sessions are decided first (they take a day from every
/// participant), then each person's own days are chosen around them.
///
/// <para><b>How a person's days are chosen.</b> The week's menu (<see cref="WorkoutFrequency.Menu"/>) says how
/// many hard sessions (strength, cardio) and how many easy ones (mobility) are wanted. The hard days are the
/// best set of days with a free gap, no two next to each other and none next to a recent hard day; "best" is
/// the most room, and when two choices are equal the earlier one wins. As many as the calendar allows are
/// placed, so a crowded week gives fewer sessions rather than a worse one. The mobility day goes on a free day
/// that is not already taken, preferring the day after a hard one. Nothing here names a weekday except Sunday, which is
/// the household's first day (<see cref="PlanningWeek"/>) and holds a gentle walk and nothing else. A hard session on the last
/// Saturday before the week meets the week only through <c>LastHardSessionDay</c>, and the Sunday it is next to is gentle anyway.</para>
/// </summary>
internal sealed class WeekPlanner
{
    private const int DaysInWeek = 7;
    private const int MostEveningsTogether = 2;
    private const double RoomCapMinutes = 60;
    private const double AfterHardBonus = 15;
    private const string NoRoom = "There was no free gap of 30 minutes or more in waking hours this week.";

    private readonly WorkoutPlanRequest request;
    private readonly TimeZoneInfo zone;
    private readonly DateOnly[] days;
    private readonly List<Person> people;
    private readonly int weekIndex;

    public WeekPlanner(WorkoutPlanRequest request)
    {
        Check(request);
        this.request = request;
        zone = request.Rules.Zone;
        days = Enumerable.Range(0, DaysInWeek).Select(request.WeekStart.AddDays).ToArray();
        people = request.People.Select(week => new Person(week)).ToList();
        // Alternates the treadmill and the road, and turns the exercises over, from one week to the next.
        weekIndex = PlanningWeek.IndexOf(request.WeekStart);
    }

    public HouseholdWorkoutPlan Plan()
    {
        foreach (var person in people.Where(item => item.IsReadable))
        {
            person.Target = WorkoutFrequency.Target(person.Week.Recovery);
            for (var day = 0; day < DaysInWeek; day++)
            {
                var (solo, evening) = SplitAtBedtime(Gaps(person, day), day);
                person.Solo[day] = solo;
                person.Evening[day] = evening;
            }
        }

        // A session shared with someone whose calendar could not be read is a guess about their evening.
        var joint = people.Count > 1 && people.All(item => item.IsReadable) ? PlanJoint() : new Dictionary<int, PlannedSession>();
        var plans = people.Select(person => PlanPerson(person, joint)).ToList();
        return new HouseholdWorkoutPlan(request.WeekStart, plans, joint.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList());
    }

    // ---- joint sessions ----

    private Dictionary<int, PlannedSession> PlanJoint()
    {
        var joint = new Dictionary<int, PlannedSession>();
        var names = people.Select(person => person.Name).ToList();

        // A family walk, on the Sunday (the week's first day) when the whole family is free before the children's routine begins.
        for (var day = 0; day < DaysInWeek; day++)
        {
            var shared = IntersectAll(people.Select(person => person.Solo[day]));
            if (days[day].DayOfWeek == DayOfWeek.Sunday && shared.Count > 0)
            {
                joint[day] = Place(day, Best(shared), SessionKind.GentleWalk, Together.FamilyWalk, names, 0);
            }
        }

        // An evening together, once the children are in bed, on a day when someone has no other room.
        // The evening is the squeezed adult's only gap, and it is only ever planned for everyone at once.
        var cap = Math.Min(MostEveningsTogether, people.Min(person => person.Target));
        var picked = new List<(int Day, FreeGap Gap)>();
        var candidates = Enumerable.Range(0, DaysInWeek)
            .Where(day => days[day].DayOfWeek != DayOfWeek.Sunday && !people.Any(person => NextToRecentHard(person, day)))
            .Where(day => people.Any(person => person.Solo[day].Count == 0))
            .Select(day => (Day: day, Shared: IntersectAll(people.Select(person => person.Evening[day]))))
            .Where(candidate => candidate.Shared.Count > 0)
            .Select(candidate => (candidate.Day, Gap: Best(candidate.Shared)))
            .OrderByDescending(candidate => Room(candidate.Gap))
            .ThenBy(candidate => candidate.Day);
        foreach (var candidate in candidates)
        {
            if (picked.Count < cap && picked.All(other => Math.Abs(other.Day - candidate.Day) >= 2))
            {
                picked.Add(candidate);
            }
        }

        var variant = 0;
        foreach (var evening in picked.OrderBy(item => item.Day))
        {
            joint[evening.Day] = Place(evening.Day, evening.Gap, SessionKind.Strength, Together.AfterBedtime, names, weekIndex + variant++);
        }

        return joint;
    }

    // ---- one person's own days ----

    private WeeklyPlan PlanPerson(Person person, Dictionary<int, PlannedSession> joint)
    {
        if (!person.IsReadable)
        {
            return new WeeklyPlan(person.Name, request.WeekStart, [], Unreadable(person));
        }

        var sessions = new List<PlannedSession>(joint.Values);
        var jointHardDays = joint.Where(pair => SessionKinds.IsHard(pair.Value.Session.Kind)).Select(pair => pair.Key).ToHashSet();
        var menu = WorkoutFrequency.Menu(person.Target).ToList();
        for (var taken = 0; taken < jointHardDays.Count && menu.Count > 0; taken++)
        {
            // A joint strength session is one of this person's sessions for the week: take it off the menu.
            var index = menu.IndexOf(SessionKind.Strength);
            menu.RemoveAt(index >= 0 ? index : menu.Count - 1);
        }

        var hardKinds = menu.Where(SessionKinds.IsHard).ToList();
        var easyCount = menu.Count - hardKinds.Count;
        var open = Enumerable.Range(0, DaysInWeek)
            .Where(day => days[day].DayOfWeek != DayOfWeek.Sunday && person.Solo[day].Count > 0 && !joint.ContainsKey(day))
            .ToList();

        var hardCandidates = open
            .Where(day => !jointHardDays.Any(other => Math.Abs(other - day) < 2) && !NextToRecentHard(person, day))
            .ToList();
        var hardDays = SpacedDays(hardCandidates, hardKinds.Count, day => Room(Best(person.Solo[day])));

        var strengthOrdinal = jointHardDays.Count;
        foreach (var (day, kind) in hardDays.Zip(hardKinds))
        {
            var chosen = kind == SessionKind.Strength ? SessionKind.Strength : CardioKind();
            var variant = chosen == SessionKind.Strength ? weekIndex + strengthOrdinal++ : 0;
            sessions.Add(Place(day, Best(person.Solo[day]), chosen, Together.None, [person.Name], variant));
        }

        var easyDays = open
            .Where(day => !hardDays.Contains(day))
            .OrderByDescending(day => Room(Best(person.Solo[day])) + (FollowsHard(person, day, hardDays, jointHardDays) ? AfterHardBonus : 0))
            .ThenBy(day => day)
            .Take(easyCount);
        foreach (var day in easyDays)
        {
            sessions.Add(Place(day, Best(person.Solo[day]), SessionKind.Mobility, Together.None, [person.Name], weekIndex));
        }

        // Sunday holds a gentle walk and nothing else (a joint family walk is already in the list).
        var sunday = Enumerable.Range(0, DaysInWeek).FirstOrDefault(day => days[day].DayOfWeek == DayOfWeek.Sunday, -1);
        if (sunday >= 0 && !joint.ContainsKey(sunday) && person.Solo[sunday].Count > 0)
        {
            sessions.Add(Place(sunday, Best(person.Solo[sunday]), SessionKind.GentleWalk, Together.None, [person.Name], 0));
        }

        var ordered = sessions.OrderBy(session => session.Start).ToList();
        return new WeeklyPlan(person.Name, request.WeekStart, ordered, ordered.Count == 0 ? NoRoom : null);
    }

    private SessionKind CardioKind() =>
        request.Equipment.Has(EquipmentCatalogue.TreadmillId) && weekIndex % 2 == 1 ? SessionKind.Treadmill : SessionKind.OutdoorCardio;

    private bool NextToRecentHard(Person person, int day) =>
        person.Week.Recovery.LastHardSessionDay is { } last && Math.Abs(days[day].DayNumber - last.DayNumber) < 2;

    private bool FollowsHard(Person person, int day, IReadOnlyCollection<int> hardDays, IReadOnlySet<int> jointHardDays) =>
        hardDays.Contains(day - 1)
        || jointHardDays.Contains(day - 1)
        || (person.Week.Recovery.LastHardSessionDay is { } last && days[day].DayNumber - last.DayNumber == 1);

    // The best set of at most `wanted` days, none next to another: as many as fit, then the most room, then the earliest.
    private static List<int> SpacedDays(IReadOnlyList<int> candidates, int wanted, Func<int, double> room)
    {
        for (var size = Math.Min(wanted, candidates.Count); size >= 1; size--)
        {
            List<int>? best = null;
            var bestRoom = double.NegativeInfinity;
            var chosen = new List<int>();

            void Search(int from)
            {
                if (chosen.Count == size)
                {
                    var total = chosen.Sum(room);
                    if (total > bestRoom)
                    {
                        best = chosen.ToList();
                        bestRoom = total;
                    }

                    return;
                }

                for (var index = from; index < candidates.Count; index++)
                {
                    if (chosen.Count > 0 && candidates[index] - chosen[^1] < 2)
                    {
                        continue;
                    }

                    chosen.Add(candidates[index]);
                    Search(index + 1);
                    chosen.RemoveAt(chosen.Count - 1);
                }
            }

            Search(0);
            if (best is not null)
            {
                return best;
            }
        }

        return [];
    }

    private PlannedSession Place(int day, FreeGap gap, SessionKind kind, Together together, IReadOnlyList<string> participants, int variant)
    {
        var duration = SessionLibrary.DurationFor(kind, together, gap.Length);
        var session = SessionLibrary.Build(kind, together, duration, request.Equipment, variant);
        return new PlannedSession(days[day], gap.Start, gap, session, participants, together);
    }

    // ---- gaps ----

    private List<FreeGap> Gaps(Person person, int day)
    {
        var settings = request.Settings;
        var opens = ZoneClock.At(days[day], settings.WakingStart, zone, ClockEdge.Start);
        var closes = ZoneClock.At(days[day], settings.WakingEnd, zone, ClockEdge.End);
        var from = request.Now > opens ? request.Now : opens;
        return closes > from ? person.Week.Busy.FreeGaps(from, closes, WorkoutPlanner.MinimumGap).ToList() : [];
    }

    // Before the children's bedtime a gap is the person's own; from bedtime on it is the evening, which is only for sessions together.
    private (List<FreeGap> Solo, List<FreeGap> Evening) SplitAtBedtime(List<FreeGap> gaps, int day)
    {
        if (request.Rules.Bedtime.IsNone)
        {
            return (gaps, []);
        }

        var bedtime = ZoneClock.At(days[day], request.Rules.Bedtime.Bedtime, zone, ClockEdge.End);
        var solo = gaps.Select(gap => new FreeGap(gap.Start, gap.End < bedtime ? gap.End : bedtime));
        var evening = gaps.Select(gap => new FreeGap(gap.Start > bedtime ? gap.Start : bedtime, gap.End));
        return (solo.Where(Fits).ToList(), evening.Where(Fits).ToList());
    }

    private static bool Fits(FreeGap gap) => gap.Length >= WorkoutPlanner.MinimumGap;

    private static List<FreeGap> IntersectAll(IEnumerable<IReadOnlyList<FreeGap>> lists)
    {
        List<FreeGap>? shared = null;
        foreach (var list in lists)
        {
            shared = shared is null ? list.ToList() : Intersect(shared, list);
        }

        return shared ?? [];
    }

    private static List<FreeGap> Intersect(IReadOnlyList<FreeGap> left, IReadOnlyList<FreeGap> right) =>
        left.SelectMany(a => right.Select(b => new FreeGap(a.Start > b.Start ? a.Start : b.Start, a.End < b.End ? a.End : b.End)))
            .Where(Fits)
            .ToList();

    // Room is how much of the gap counts: past an hour more room makes no better a session for a beginner.
    private static double Room(FreeGap gap) => Math.Min(gap.Length.TotalMinutes, RoomCapMinutes);

    private static FreeGap Best(IReadOnlyList<FreeGap> gaps) =>
        gaps.OrderByDescending(Room).ThenByDescending(gap => gap.Start).First();

    // ---- the request ----

    private static string Unreadable(Person person)
    {
        var reasons = person.Week.Busy.Unreadable.Select(item => $"{Describe(item.Source)}: {item.Reason}");
        return $"{person.Name}'s calendars could not all be read ({string.Join("; ", reasons)}), so no workouts were planned.";
    }

    private static string Describe(CalendarSource source) => source switch
    {
        CalendarSource.Own => "their own calendar",
        CalendarSource.Family => "the family calendar",
        CalendarSource.Children => "the children's calendars",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown calendar source."),
    };

    private static void Check(WorkoutPlanRequest request)
    {
        if (request.Rules is null)
        {
            throw new ArgumentException("The planner needs the household's rules.", nameof(request));
        }

        if (request.Equipment is null)
        {
            throw new ArgumentException("The planner needs the equipment checklist.", nameof(request));
        }

        if (request.People is null || request.People.Count == 0)
        {
            throw new ArgumentException("There is no one to plan for.", nameof(request));
        }

        if (request.Settings.WakingEnd <= request.Settings.WakingStart)
        {
            throw new ArgumentException("The waking hours have to end after they begin.", nameof(request));
        }

        var duplicate = request.People
            .GroupBy(week => week.Busy.Person, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"{duplicate.Key} appears twice in the request.", nameof(request));
        }

        foreach (var week in request.People)
        {
            if (request.Rules.FindAdult(week.Busy.Person) is null)
            {
                throw new ArgumentException($"{week.Busy.Person} is not one of the household's adults.", nameof(request));
            }

            if (week.Recovery.SessionsDoneLastWeek < 0 || week.Recovery.SessionsSkippedLastWeek < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), $"{week.Busy.Person}'s history counts sessions, so it cannot be negative.");
            }
        }
    }

    private sealed class Person
    {
        public Person(PersonWeek week)
        {
            Week = week;
            for (var day = 0; day < DaysInWeek; day++)
            {
                Solo[day] = [];
                Evening[day] = [];
            }
        }

        public PersonWeek Week { get; }

        public string Name => Week.Busy.Person;

        public bool IsReadable => Week.Busy.IsComplete;

        public List<FreeGap>[] Solo { get; } = new List<FreeGap>[DaysInWeek];

        public List<FreeGap>[] Evening { get; } = new List<FreeGap>[DaysInWeek];

        public int Target { get; set; }
    }
}
