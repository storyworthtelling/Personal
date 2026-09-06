namespace Storyworthtelling.Maui.State;

/// <summary>
/// Which colour a fold carries, as a token name rather than a value.
///
/// <para>Pure so the mapping is testable and so nothing here holds a hex: the view looks the name
/// up in the merged dictionary, which is the registered token file. A fold that mixed its own
/// colour would be a component drifting from the role table it is an instance of.</para>
/// </summary>
public static class DogEarPalette
{
    public static string TokenFor(DogEarHue hue) => hue switch
    {
        DogEarHue.Problem => "ProblemMark",
        DogEarHue.Caution => "CautionMark",
        DogEarHue.Resolved => "ResolvedMark",
        DogEarHue.Action => "ActionMark",
        _ => throw new ArgumentOutOfRangeException(nameof(hue), hue, "No token is registered for this hue."),
    };
}
