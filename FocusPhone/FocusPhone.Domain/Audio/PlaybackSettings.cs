namespace FocusPhone.Domain.Audio;

/// <summary>
/// Per-show podcast playback preferences. Speed is remembered per show and
/// adjusted in fine 0.1x steps; skip-silence and loudness normalization are
/// toggles. Kept a small record so it is trivially unit-tested and persisted.
/// </summary>
public record PlaybackSettings(
    float Speed = 1.0f,
    bool SkipSilence = false,
    bool NormalizeLoudness = false)
{
    public const float MinSpeed = 0.5f;
    public const float MaxSpeed = 3.0f;
    public const float Step = 0.1f;

    /// <summary>The preset speeds offered as one-tap buttons.</summary>
    public static readonly IReadOnlyList<float> Presets = [1.0f, 1.2f, 1.5f, 2.0f];

    public PlaybackSettings WithSpeed(float speed) => this with { Speed = Clamp(speed) };

    public PlaybackSettings Faster() => WithSpeed(Speed + Step);

    public PlaybackSettings Slower() => WithSpeed(Speed - Step);

    private static float Clamp(float speed) =>
        Math.Clamp(SnapToStep(speed), MinSpeed, MaxSpeed);

    // Snap to the nearest 0.1 so accumulated float drift can't produce speeds like 1.4000001.
    private static float SnapToStep(float speed) => MathF.Round(speed * 10f) / 10f;
}
