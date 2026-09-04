namespace Codex.Domain.Home;

/// <summary>
/// Hand angles for the drawn analog clock (DESIGN.md §11: the home screen
/// tells time the analog way). Pure math so the launcher's custom view stays
/// a thin drawing shell. Angles are degrees clockwise from 12 o'clock.
/// </summary>
public static class ClockAngles
{
    public static (float HourDegrees, float MinuteDegrees) FromTime(TimeSpan timeOfDay)
    {
        var minutes = timeOfDay.Minutes + timeOfDay.Seconds / 60f;
        var hours = timeOfDay.Hours % 12 + minutes / 60f;
        return (hours * 30f, minutes * 6f);
    }
}
