namespace Codex.Domain.Music;

/// <summary>
/// The now-playing scrubber's math and labels: the elapsed/remaining clock strings and the 0–1 progress
/// fraction, plus the seek target for a scrub. Pure so the now-playing screen stays a thin adapter. Music
/// clocks are m:ss (h:mm:ss past an hour) — distinct from the podcast "min left" of <see cref="Audio.PlaybackFormatting"/>.
/// </summary>
public static class PlaybackTimeline
{
    public static double Fraction(TimeSpan position, TimeSpan duration) =>
        duration > TimeSpan.Zero ? Math.Clamp(position / duration, 0.0, 1.0) : 0.0;

    public static TimeSpan SeekTarget(double fraction, TimeSpan duration) =>
        duration > TimeSpan.Zero ? duration * Math.Clamp(fraction, 0.0, 1.0) : TimeSpan.Zero;

    public static string Elapsed(TimeSpan position) => Clock(position);

    public static string Remaining(TimeSpan position, TimeSpan duration)
    {
        var left = duration - position;
        if (left < TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
        }

        return "-" + Clock(left);
    }

    private static string Clock(TimeSpan value)
    {
        var total = (int)Math.Floor(value.TotalSeconds);
        if (total < 0)
        {
            total = 0;
        }

        var minutes = total / 60;
        var seconds = total % 60;
        if (minutes >= 60)
        {
            var hours = minutes / 60;
            minutes %= 60;
            return $"{hours}:{minutes:00}:{seconds:00}";
        }

        return $"{minutes}:{seconds:00}";
    }
}
