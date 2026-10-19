using Constitution.Domain.Equipment;

namespace Constitution.Domain.Workouts;

/// <summary>A hard session turned to mobility, for the card and Today's row. <see cref="Reason"/> is words only.</summary>
public sealed record RecoverySwapNote(IReadOnlyList<string> People, DateOnly Day, SessionKind Was, string Reason);

public sealed record RecoveryAdjusted(HouseholdWorkoutPlan Plan, IReadOnlyList<RecoverySwapNote> Swaps);

/// <summary>
/// What an under-recovered morning does to the plan (C-5.2.4). Two doors, both onto things that already exist.
///
/// <para><see cref="Apply"/> is the morning's: a hard session (strength or cardio) that has <i>not begun</i> and falls on
/// today becomes mobility in the same slot, for whoever is under-recovered. A shared session is one object, so it becomes
/// mobility for everyone in it. Mobility and walks are left alone, and a session already begun is never replaced (the
/// same rule as C-2.3.6). A morning no one could judge, or a recovered one, returns the plan as the same object.</para>
///
/// <para><see cref="Ease"/> is the planning-time one: it lowers the week's frequency through the planner's <i>existing</i>
/// input, by raising <see cref="RecoveryInputs.SessionsSkippedLastWeek"/> to the count that eases
/// (<see cref="WorkoutFrequency.SkipsThatEase"/>). That is an honest stand-in rather than a true count of skips: no
/// planner change was made for this PBI, and a dedicated field is the cleaner shape if the owner wants one.</para>
/// </summary>
public static class RecoverySwap
{
    public static RecoveryAdjusted Apply(
        HouseholdWorkoutPlan plan,
        IReadOnlyDictionary<string, RecoveryRead> reads,
        DateOnly today,
        DateTimeOffset now,
        EquipmentSet equipment)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(equipment);

        var swaps = new List<RecoverySwapNote>();
        var rewritten = RecoveryPlanEdit.Rewrite(plan, "Nothing is planned.", session =>
        {
            if (session.Day != today || !SessionKinds.IsHard(session.Session.Kind) || session.Start <= now)
            {
                return session;
            }

            var tired = session.Participants
                .Select(person => ReadOf(reads, person))
                .OfType<RecoveryRead>()
                .Where(read => read.State is RecoveryState.UnderRecovered or RecoveryState.Alarming)
                .ToList();
            if (tired.Count == 0)
            {
                return session;
            }

            var duration = SessionLibrary.DurationFor(SessionKind.Mobility, session.Together, session.Gap.Length);
            var mobility = SessionLibrary.Build(SessionKind.Mobility, session.Together, duration, equipment, 0);
            swaps.Add(new RecoverySwapNote(session.Participants, session.Day, session.Session.Kind, Line(tired.SelectMany(read => read.Reasons).Distinct().Order().ToList())));
            return session with { Session = mobility };
        });

        return new RecoveryAdjusted(rewritten, swaps);
    }

    public static RecoveryInputs Ease(RecoveryInputs basis, RecoveryRead read)
    {
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(read);
        return read.State is RecoveryState.UnderRecovered or RecoveryState.Alarming
            ? basis with { SessionsSkippedLastWeek = Math.Max(basis.SessionsSkippedLastWeek, WorkoutFrequency.SkipsThatEase) }
            : basis;
    }

    private static RecoveryRead? ReadOf(IReadOnlyDictionary<string, RecoveryRead> reads, string person) =>
        reads.FirstOrDefault(pair => string.Equals(pair.Key?.Trim(), person.Trim(), StringComparison.OrdinalIgnoreCase)).Value;

    private static string Line(IReadOnlyList<RecoveryReason> reasons)
    {
        var words = reasons.Select(reason => reason switch
        {
            RecoveryReason.LowHeartRateVariability => "low heart rate variability",
            RecoveryReason.ShortSleep => "short sleep",
            RecoveryReason.RaisedRestingHeartRate => "raised resting heart rate",
            _ => throw new ArgumentOutOfRangeException(nameof(reasons), reason, "That is not a reason the line knows."),
        }).ToList();

        var named = words.Count switch
        {
            0 => "not recovered",
            1 => words[0],
            _ => string.Join(", ", words.Take(words.Count - 1)) + " and " + words[^1],
        };
        return $"Mobility today: {named}";
    }
}
