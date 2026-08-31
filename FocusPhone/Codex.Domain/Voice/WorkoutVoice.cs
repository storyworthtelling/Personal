namespace Codex.Domain.Voice;

/// <summary>
/// What the voice says over a workout, and how often it says each kind.
///
/// The register is the thing to get right. The owner's profile bans sincere
/// exclamation marks, hype and explaining the joke — which rules out everything a
/// fitness app says. What is left, and what is actually funny, is a deadpan person
/// <b>performing</b> a coach: the line does the encouragement and then quietly takes
/// it back, which is the profile's signature shape rather than a cheer.
///
/// Three categories, one per track. The weights are a judgement rather than the
/// owner's: coaching carries the run, the pun needs only a title so it is always
/// available, and the over-the-top congratulation is rationed because that is the
/// entire joke — it is funny when it arrives for no reason, and tiresome when it
/// arrives every time.
/// </summary>
public static class WorkoutVoice
{
    public const string Coaching = "coaching";
    public const string Pun = "pun";
    public const string Congratulation = "congratulation";

    public static IReadOnlyList<string> Categories { get; } = [Coaching, Pun, Congratulation];

    public static IReadOnlyDictionary<string, int> Weights { get; } = new Dictionary<string, int>
    {
        [Coaching] = 5,
        [Pun] = 3,
        [Congratulation] = 2,
    };

    /// <summary>What the voice is being asked for. Unknown categories fall back to
    /// coaching rather than throwing at the one moment nobody can read a stack trace.</summary>
    public static string Purpose(string category) => category switch
    {
        Pun =>
            "one line about the track now playing, built on its title or its words, delivered flat — " +
            "a pun the speaker is not going to acknowledge having made",
        Congratulation =>
            "one line congratulating the listener out of all proportion to what they have done, " +
            "then quietly withdrawing the claim",
        _ =>
            "one line of encouragement from someone performing the part of a coach and not quite " +
            "committing to it — it lands on a flat retraction rather than a cheer",
    };
}
