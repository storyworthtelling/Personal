namespace DailyLoader.Worker;

public class ScheduleOptions
{
    public const string SectionName = "Schedule";
    public int DeliveryHour { get; set; } = 6;
    public int DeliveryMinute { get; set; } = 0;
    public string TimeZoneId { get; set; } = "America/New_York";
}
