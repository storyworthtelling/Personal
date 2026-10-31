using System.Globalization;
using Codex.Domain.Music;
using Constitution.Domain.Equipment;
using Constitution.Domain.Time;

namespace Constitution.Domain.Workouts.Screens;

/// <summary>The words the Workouts tab prints, one way each (book p.12): kind names, kit, Score line, minutes.</summary>
internal static class WorkoutWords
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Kind(PlannedSession session) => KindWords.Of(session.Session.Kind, session.Together);

    public static int Minutes(PlannedSession session) => (int)Math.Round(session.Session.Duration.TotalMinutes);

    public static string Clock(DateTimeOffset moment) => TimeFormats.Clock12(moment.DateTime);

    public static string ShortDay(DateOnly day) => day.ToString("ddd", Invariant);

    public static string LongDay(DateOnly day) => day.ToString("dddd", Invariant);

    /// <summary>"Score 132 bpm · Steady": the setting and its band from <see cref="TempoBands.Of"/>.</summary>
    public static string Score(int bpm) =>
        $"Score {bpm.ToString(Invariant)} bpm · {TempoBands.Names[(int)TempoBands.Of(bpm)]}";

    /// <summary>"7 exercises · dumbbells, bands, bench": the count, then the kit it needs, if any.</summary>
    public static string Kit(Session session)
    {
        var count = session.Exercises.Count;
        var exercises = $"{count.ToString(Invariant)} {(count == 1 ? "exercise" : "exercises")}";
        if (session.EquipmentNeeded.Count == 0)
        {
            return exercises;
        }

        return exercises + " · " + string.Join(", ", session.EquipmentNeeded.Select(Equipment));
    }

    private static string Equipment(string id) => id switch
    {
        EquipmentCatalogue.DumbbellsId => "dumbbells",
        EquipmentCatalogue.BandsId => "bands",
        EquipmentCatalogue.TreadmillId => "treadmill",
        EquipmentCatalogue.BenchId => "bench",
        EquipmentCatalogue.PullUpBarId => "pull-up bar",
        EquipmentCatalogue.MatId => "mat",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "The tab has no word for this equipment."),
    };

    /// <summary>Who else is on a shared session, in a phrase: "with everyone" for a family walk, "with Lee" otherwise.</summary>
    public static string? With(PlannedSession session, string person)
    {
        if (session.Together == Together.FamilyWalk)
        {
            return "with everyone";
        }

        var others = session.Participants.Where(name => !string.Equals(name, person, StringComparison.OrdinalIgnoreCase)).ToList();
        return others.Count == 0 ? null : "with " + string.Join(" and ", others);
    }
}
