using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NytRemarkable.Infrastructure.Delivery;

namespace NytRemarkable.Worker;

public class CrosswordScheduler(
    CrosswordDeliveryService deliveryService,
    IOptions<ScheduleOptions> options,
    ILogger<CrosswordScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Crossword scheduler started");

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

        logger.LogInformation("Crossword scheduler stopped");
    }

    private async Task RunDelivery(DateOnly date)
    {
        try
        {
            await deliveryService.DeliverDailyPuzzle(date);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver daily crossword for {Date}", date);
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
