namespace Foreword.Infrastructure.Google;

// OAuth credentials (one-time consent on Matt's account) plus calendar-creation settings.
public class GoogleOptions
{
    public const string SectionName = "Google";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;

    // Time zone used to interpret a calendar "day" and to place events on the timeline.
    public string TimeZoneId { get; set; } = "America/New_York";

    // Calendar that transcribed events are written to (the user's primary by default).
    public string AddEventCalendarId { get; set; } = "primary";

    // Summaries for the kid calendars auto-created on first run.
    public string GwenCalendarName { get; set; } = "Gwen";
    public string EvaCalendarName { get; set; } = "Eva";
}
