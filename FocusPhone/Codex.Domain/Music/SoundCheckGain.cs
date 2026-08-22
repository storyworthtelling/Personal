namespace Codex.Domain.Music;

/// <summary>
/// "Sound Check" loudness normalization: the gain (in millibels, for Android's LoudnessEnhancer) that pulls
/// a track toward a reference loudness, so albums and singles play at an even volume. A quieter-than-reference
/// track is boosted, a louder one cut, and the adjustment is clamped so normalization never blows out or
/// buries a track. Pure.
/// </summary>
public static class SoundCheckGain
{
    /// <param name="trackLufs">The track's integrated loudness (LUFS, negative).</param>
    /// <param name="referenceLufs">The target loudness (e.g. -14 LUFS).</param>
    /// <param name="maxBoostMillibels">Largest positive adjustment allowed (>= 0).</param>
    /// <param name="maxCutMillibels">Largest negative adjustment allowed, as a magnitude (>= 0).</param>
    public static int Millibels(double trackLufs, double referenceLufs, int maxBoostMillibels, int maxCutMillibels)
    {
        var decibels = referenceLufs - trackLufs; // positive when the track is quieter than reference
        var millibels = (int)Math.Round(decibels * 100, MidpointRounding.AwayFromZero);
        return Math.Clamp(millibels, -Math.Abs(maxCutMillibels), Math.Abs(maxBoostMillibels));
    }
}
