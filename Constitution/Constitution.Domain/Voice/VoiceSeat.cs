namespace Constitution.Domain.Voice;

/// <summary>
/// Every place Constitution speaks in the owner's voice (DESIGN.md §14, book p.18) — exactly six, a
/// closed list, so a new voiced surface is a decision rather than a diff.
///
/// <para><b>Errors are not among them, and never will be.</b> Failures fold into the dog-ear in plain
/// copy (a Problem notice, the rose bookmark); the voice is for the app's own remarks, not its bad
/// news. <see cref="VoicedOccasions"/> gives each seat its stored id and purpose.</para>
///
/// <para>The enum's numbers are never stored — the ids in <see cref="VoicedOccasions"/> are — so the
/// order here is only the order the book lists them.</para>
/// </summary>
public enum VoiceSeat
{
    /// <summary>The line at the top of Today on a day with a workout.</summary>
    TodayTopLine,

    /// <summary>The line at the top of Today on a rest day.</summary>
    RestDayLine,

    /// <summary>Body's one sentence on how the month moved (with the device reminder the day after).</summary>
    BodyMonthly,

    /// <summary>A signal's own sentence, set against the owner's own past.</summary>
    SignalSentence,

    /// <summary>The encouragement on each workout page.</summary>
    WorkoutEncouragement,

    /// <summary>The intro to the weekly meal sheet.</summary>
    MealSheetIntro,
}
