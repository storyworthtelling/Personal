using Android.Content;
using Android.OS;
using Watch.Domain.Design;
using Watch.Domain.Touch;

namespace Watch.Ui.Wear;

/// <summary>
/// The wrist's touch (WD §7, A35): plays one of the four token shapes (<see cref="WatchTokens.Haptics"/>) as it is, its
/// timings and strengths, and nothing of its own. A notification's buzz is one the platform lets a background app play,
/// and mutes for the silent ringer and the watch's own Do Not Disturb; a request to talk (<see cref="BuzzUsage.CommunicationRequest"/>)
/// is one that Do Not Disturb lets through when it lets calls through. Whether to buzz at all is the haptic plan's.
/// </summary>
public static class WatchBuzz
{
    /// <summary>Plays the pattern; false when the watch has no vibrator to play it on.</summary>
    public static bool Play(Context context, HapticPattern pattern, BuzzUsage usage)
    {
        if (context.GetSystemService(Context.VibratorManagerService) is not VibratorManager vibrators || vibrators.DefaultVibrator is not { HasVibrator: true } vibrator)
        {
            return false;
        }

        var timings = pattern.Timings.Select(milliseconds => (long)milliseconds).ToArray();
        var repeat = pattern.Repeats ? 0 : -1;
        // A vibrator without strength control plays the same rhythm at its one strength: its waveform starts with a pause.
        var effect = vibrator.HasAmplitudeControl
            ? VibrationEffect.CreateWaveform(timings, [.. pattern.Strengths], repeat)
            : VibrationEffect.CreateWaveform([0, .. timings], repeat < 0 ? repeat : repeat + 1);
        var attributes = new VibrationAttributes.Builder()
            .SetUsage((int)(usage == BuzzUsage.CommunicationRequest ? VibrationAttributesUsageType.CommunicationRequest : VibrationAttributesUsageType.Notification))!
            .Build()!;
        vibrator.Vibrate(effect!, attributes);
        return true;
    }
}
