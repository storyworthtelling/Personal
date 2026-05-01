using Microsoft.Extensions.Logging;
using NytRemarkable.Domain.Interfaces;

namespace NytRemarkable.Worker;

public class CrosswordDeliveryService(
    INytCrosswordClient nytClient,
    IRemarkableClient remarkableClient,
    ILogger<CrosswordDeliveryService> logger)
{
    public async Task DeliverDailyPuzzle(DateOnly date)
    {
        logger.LogInformation("Downloading daily crossword for {Date}", date);
        var pdfBytes = await nytClient.DownloadDailyPuzzle(date);

        var title = $"NYT Crossword {date:yyyy-MM-dd}";
        logger.LogInformation("Uploading to reMarkable: {Title}", title);
        await remarkableClient.Upload(title, pdfBytes);

        logger.LogInformation("Delivered: {Title}", title);
    }

    public async Task DeliverBonusPuzzle(DateOnly date)
    {
        logger.LogInformation("Downloading bonus crossword for {Date}", date);
        var pdfBytes = await nytClient.DownloadBonusPuzzle(date);

        var title = $"NYT Bonus Crossword {date:MMMM yyyy}";
        logger.LogInformation("Uploading to reMarkable: {Title}", title);
        await remarkableClient.Upload(title, pdfBytes);

        logger.LogInformation("Delivered: {Title}", title);
    }
}
