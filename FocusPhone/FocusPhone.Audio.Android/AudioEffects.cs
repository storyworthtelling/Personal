using Android.Media.Audiofx;
using FocusPhone.Domain.Music;

namespace FocusPhone.Audio;

/// <summary>
/// Owns the Android <see cref="Equalizer"/> + <see cref="LoudnessEnhancer"/> attached to the player's audio
/// session, and applies the saved <see cref="EqualizerSettings"/> + loudness boost. The session id changes
/// across playback, so <see cref="Apply"/> re-binds when it does. Best-effort: some devices reject audio
/// effects, so every attach/set is guarded and simply skipped on failure rather than crashing playback.
/// </summary>
public sealed class AudioEffects : IDisposable
{
    private Equalizer? equalizer;
    private LoudnessEnhancer? loudness;
    private int sessionId = -1;

    public void Apply(int audioSessionId, EqualizerSettings settings, int loudnessMillibels)
    {
        if (audioSessionId == 0)
        {
            return; // no real session yet
        }

        try
        {
            if (sessionId != audioSessionId)
            {
                Release();
                sessionId = audioSessionId;
                equalizer = new Equalizer(0, audioSessionId);
                loudness = new LoudnessEnhancer(audioSessionId);
            }

            ApplyEqualizer(settings);
            ApplyLoudness(loudnessMillibels);
        }
        catch (Exception e) when (e is Java.Lang.RuntimeException or Java.Lang.UnsupportedOperationException)
        {
            // Effects are a nicety; a device that refuses them shouldn't break playback.
            Release();
        }
    }

    private void ApplyEqualizer(EqualizerSettings settings)
    {
        if (equalizer is null)
        {
            return;
        }

        equalizer.SetEnabled(settings.Enabled);
        if (!settings.Enabled)
        {
            return;
        }

        var bands = Math.Min(settings.BandGains.Count, (int)equalizer.NumberOfBands);
        var range = equalizer.GetBandLevelRange();
        short min = range is { Length: >= 2 } ? range[0] : (short)-1500;
        short max = range is { Length: >= 2 } ? range[1] : (short)1500;
        for (short band = 0; band < bands; band++)
        {
            var level = (short)Math.Clamp(settings.BandGains[band], min, max);
            equalizer.SetBandLevel(band, level);
        }
    }

    private void ApplyLoudness(int millibels)
    {
        if (loudness is null)
        {
            return;
        }

        loudness.SetEnabled(millibels > 0);
        if (millibels > 0)
        {
            loudness.SetTargetGain(millibels);
        }
    }

    public void Release()
    {
        equalizer?.Release();
        loudness?.Release();
        equalizer = null;
        loudness = null;
        sessionId = -1;
    }

    public void Dispose() => Release();
}
