namespace DailyLoader.Worker;

public static class ScheduleHelper
{
    public static bool IsFirstSundayOfMonth(DateOnly date) =>
        date.DayOfWeek == DayOfWeek.Sunday && date.Day <= 7;

    public static DateTimeOffset NextDeliveryTime(DateTimeOffset now, TimeOnly deliveryTime, TimeZoneInfo timeZone)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);

        var todayDeliveryLocal = localDate.ToDateTime(deliveryTime);
        var todayDelivery = new DateTimeOffset(todayDeliveryLocal, timeZone.GetUtcOffset(todayDeliveryLocal));

        return now < todayDelivery ? todayDelivery : todayDelivery.AddDays(1);
    }
}
