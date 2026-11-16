namespace Storyworthtelling.CallSheet;

/// <summary>Which Claude model writes a line: the strong one where the owner reads it for its voice, the fast one
/// for small, frequent lines.</summary>
public enum VoiceTier
{
    Fast,
    Strong,
}

/// <summary>What a seat shows when there is no voice profile, no key, or no line yet (DESIGN.md §14).</summary>
public enum VoiceFallback
{
    /// <summary>Nothing: the line is simply not there.</summary>
    Hide,

    /// <summary>A plain line in its place: the sheet's <see cref="VoiceLine.Neutral"/>, or the line the app drew.</summary>
    Neutral,
}

/// <summary>Who a line speaks to. Only Pulp speaks to a child, at the child's reading level.</summary>
public enum VoiceAudience
{
    Adult,
    Child,
}

/// <summary>
/// One voiced line in the suite.
/// </summary>
/// <param name="App">The app it appears in.</param>
/// <param name="Seat">Its stored id: lower-case words and hyphens, since caches key on it.</param>
/// <param name="Purpose">The occasion, as Claude is told it (the PURPOSE of the request).</param>
/// <param name="MaxWords">The word limit Claude is given.</param>
/// <param name="Tier">Which model writes it.</param>
/// <param name="Fallback">What shows without a voice.</param>
/// <param name="Audience">Who reads it.</param>
/// <param name="Neutral">The plain line shown in its place, where the app does not draw its own.</param>
/// <param name="Guidance">Any standing instruction beyond the purpose: what the line may and may not say.</param>
public sealed record VoiceLine(
    string App,
    string Seat,
    string Purpose,
    int MaxWords,
    VoiceTier Tier,
    VoiceFallback Fallback,
    VoiceAudience Audience = VoiceAudience.Adult,
    string? Neutral = null,
    string? Guidance = null)
{
    /// <summary>The strong model, in the voice library's terms.</summary>
    public bool HighVisibility => Tier == VoiceTier.Strong;
}
