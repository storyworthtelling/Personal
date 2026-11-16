namespace Constitution.Domain.Voice;

/// <summary>
/// One voiced seat: its stored id, what the model is asked to write, and how long it may run, all read
/// from the suite's voice-line Call Sheet (audit W10.3).
/// </summary>
public sealed record VoicedOccasion(VoiceSeat Seat, Storyworthtelling.CallSheet.VoiceLine Line)
{
    /// <summary>The stored id: the Call Sheet's seat.</summary>
    public string Id => Line.Seat;

    public string Purpose => Line.Purpose;

    public int MaxWords => Line.MaxWords;
}

/// <summary>
/// Constitution's six voiced seats, each once (book p.18, brief §Voice seats).
///
/// <para><b>The ids are stored</b> (the daily cache keys on them), so each is a string literal,
/// never <c>nameof</c> or an enum name: a harmless-looking rename must not change what is stored.
/// <c>VoicedOccasionTests</c> holds the expected list as its own literals.</para>
///
/// <para><b>Errors are not here, and that is the rule.</b> A voice is for the app's remarks, not its
/// bad news: a failed sync, a refused permission or a bed that will not answer is a fact to act on,
/// and dressing it in the owner's wit makes it read as banter about their health — and puts a model
/// call on the path of reporting that something failed. Purposes below are therefore all the app's
/// own remarks, and a guard fails on any that reads as trouble.</para>
///
/// <para>Every request is built from facts already reduced to words; a seat is never handed a raw
/// reading, an identity or anything private.</para>
/// </summary>
public static class VoicedOccasions
{
    public static IReadOnlyList<VoicedOccasion> All { get; } =
    [
        new(VoiceSeat.TodayTopLine, Sheet("today-top-line")),
        new(VoiceSeat.RestDayLine, Sheet("rest-day-line")),
        new(VoiceSeat.BodyMonthly, Sheet("body-monthly")),
        new(VoiceSeat.SignalSentence, Sheet("signal-sentence")),
        new(VoiceSeat.WorkoutEncouragement, Sheet("workout-encouragement")),
        new(VoiceSeat.MealSheetIntro, Sheet("meal-sheet-intro")),
    ];

    // Each purpose and word limit is the suite's voice-line Call Sheet's (audit W10.3).
    private static Storyworthtelling.CallSheet.VoiceLine Sheet(string id) =>
        Storyworthtelling.CallSheet.CallSheet.For("Constitution", id);

    private static readonly Dictionary<VoiceSeat, VoicedOccasion> BySeat = All.ToDictionary(occasion => occasion.Seat);

    private static readonly Dictionary<string, VoicedOccasion> ById = All.ToDictionary(occasion => occasion.Id, StringComparer.Ordinal);

    public static VoicedOccasion For(VoiceSeat seat) =>
        BySeat.TryGetValue(seat, out var occasion)
            ? occasion
            : throw new ArgumentOutOfRangeException(nameof(seat), seat,
                "This seat is not on Constitution's closed list of six voiced seats; a voiced seat is a decision, not a diff.");

    /// <summary>
    /// The occasion for a stored id, or null for one this build does not know (an older or newer
    /// build wrote it). Unknown is unknown — it is never guessed at, and never voiced.
    /// </summary>
    public static VoicedOccasion? TryGetById(string id) => ById.GetValueOrDefault(id);
}
