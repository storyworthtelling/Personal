using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DailyLoader.Infrastructure.Delivery;

namespace DailyLoader.Worker;

public class CrosswordScheduler(
    CrosswordDeliveryService deliveryService,
    DailyDeliveryService dailyDeliveryService,
    IOptions<ScheduleOptions> options,
    ILogger<CrosswordScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Daily scheduler started");

        try
        {
            await dailyDeliveryService.EnsureKidCalendars();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to ensure kid calendars at startup");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var scheduleOptions = options.Value;
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(scheduleOptions.TimeZoneId);
            var deliveryTime = new TimeOnly(scheduleOptions.DeliveryHour, scheduleOptions.DeliveryMinute);
            var next = ScheduleHelper.NextDeliveryTime(DateTimeOffset.UtcNow, deliveryTime, timeZone);
            var delay = next - DateTimeOffset.UtcNow;

            logger.LogInformation("Next delivery at {Time} ({Delay:hh\\:mm} from now)", next.ToLocalTime(), delay);

            await Task.Delay(delay, stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;

            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone));
            await RunDelivery(date);
        }

        logger.LogInformation("Daily scheduler stopped");
    }

    private async Task RunDelivery(DateOnly date)
    {
        // Each document set is delivered independently so one failure does not block the others.
        try
        {
            await deliveryService.DeliverDailyPuzzle(date);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver daily crossword for {Date}", date);
        }

        try
        {
            await dailyDeliveryService.DeliverDailyDocuments(date);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver calendar/checklist for {Date}", date);
        }

        if (!ScheduleHelper.IsFirstSundayOfMonth(date))
            return;

        try
        {
            await deliveryService.DeliverBonusPuzzle(date);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver bonus crossword for {Date}", date);
        }
    }
}
