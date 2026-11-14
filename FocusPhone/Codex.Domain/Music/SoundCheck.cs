namespace Codex.Domain.Music;

/// <summary>How loud the next track plays: the enhancer's boost, and the player's own volume for a cut.</summary>
public readonly record struct LoudnessPlan(int BoostMillibels, float Volume);

/// <summary>
/// Sound check from a track's ReplayGain tag (audit W7.3). ReplayGain already says how far a track sits from
/// its reference loudness, so the gain is used as it is, clamped by <see cref="SoundCheckGain"/>. Android's
/// loudness enhancer only raises, so a quiet track is boosted there (on top of the owner's manual boost) and a
/// loud one is cut through the player's volume. A track with no tag plays as it is.
/// </summary>
public static class SoundCheck
{
    public const int MaxBoostMillibels = 600;
    public const int MaxCutMillibels = 1200;

    /// <summary>The quietest the player is ever turned down to, a 12 dB cut.</summary>
    public static readonly float QuietestVolume = (float)Math.Pow(10, -MaxCutMillibels / 2000.0);

    // ReplayGain's reference; any reference gives the same adjustment, since the tag is already relative to it.
    private const double ReferenceLufs = -18;

    public static LoudnessPlan Plan(double? replayGainDb, bool enabled, int manualBoostMillibels)
    {
        var manual = Math.Max(0, manualBoostMillibels);
        if (!enabled || replayGainDb is not { } gain || double.IsNaN(gain))
        {
            return new LoudnessPlan(manual, 1f);
        }

        var millibels = SoundCheckGain.Millibels(ReferenceLufs - gain, ReferenceLufs, MaxBoostMillibels, MaxCutMillibels);
        return millibels >= 0
            ? new LoudnessPlan(manual + millibels, 1f)
            : new LoudnessPlan(manual, (float)Math.Pow(10, millibels / 2000.0));
    }
}
