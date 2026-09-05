namespace Codex.Domain.Audio;

/// <summary>
/// Formats an episode's remaining time as the user will actually experience it —
/// scaled to their listening speed — so the list reads "true time left" rather
/// than raw duration. Pure and unit-tested.
/// </summary>
public static class PlaybackFormatting
{
    public static string TimeLeftAtSpeed(TimeSpan remaining, float speed)
    {
        var effectiveSpeed = speed > 0 ? speed : 1.0f;
        var adjustedMinutes = remaining.TotalMinutes / effectiveSpeed;
        var minutes = Math.Max(1, (int)Math.Round(adjustedMinutes, MidpointRounding.AwayFromZero));
        return $"{minutes} min left";
    }
}
