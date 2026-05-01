using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NytRemarkable.Domain.Interfaces;
using NytRemarkable.Worker;
using Xunit;

namespace NytRemarkable.Tests;

public class CrosswordDeliveryServiceTests
{
    private readonly Mock<INytCrosswordClient> nytClient = new();
    private readonly Mock<IRemarkableClient> remarkableClient = new();

    private CrosswordDeliveryService CreateService() =>
        new(nytClient.Object, remarkableClient.Object, NullLogger<CrosswordDeliveryService>.Instance);

    [Fact]
    public async Task DeliverDailyPuzzle_DownloadsPuzzleForGivenDate()
    {
        var date = new DateOnly(2025, 4, 14);
        nytClient.Setup(c => c.DownloadDailyPuzzle(date)).ReturnsAsync([1, 2, 3]);

        await CreateService().DeliverDailyPuzzle(date);

        nytClient.Verify(c => c.DownloadDailyPuzzle(date), Times.Once);
    }

    [Fact]
    public async Task DeliverDailyPuzzle_UploadsPdfWithDateInTitle()
    {
        var date = new DateOnly(2025, 4, 14);
        nytClient.Setup(c => c.DownloadDailyPuzzle(date)).ReturnsAsync([1, 2, 3]);

        await CreateService().DeliverDailyPuzzle(date);

        remarkableClient.Verify(c => c.Upload(
            It.Is<string>(t => t.Contains("2025-04-14")),
            It.IsAny<byte[]>()),
            Times.Once);
    }

    [Fact]
    public async Task DeliverDailyPuzzle_UploadsBytesFromNytClient()
    {
        var date = new DateOnly(2025, 4, 14);
        var expectedBytes = new byte[] { 9, 8, 7 };
        nytClient.Setup(c => c.DownloadDailyPuzzle(date)).ReturnsAsync(expectedBytes);

        await CreateService().DeliverDailyPuzzle(date);

        remarkableClient.Verify(c => c.Upload(It.IsAny<string>(), expectedBytes), Times.Once);
    }

    [Fact]
    public async Task DeliverBonusPuzzle_DownloadsBonusPuzzleForGivenDate()
    {
        var date = new DateOnly(2025, 6, 1);
        nytClient.Setup(c => c.DownloadBonusPuzzle(date)).ReturnsAsync([1]);

        await CreateService().DeliverBonusPuzzle(date);

        nytClient.Verify(c => c.DownloadBonusPuzzle(date), Times.Once);
    }

    [Fact]
    public async Task DeliverBonusPuzzle_UploadsPdfWithMonthAndYearInTitle()
    {
        var date = new DateOnly(2025, 6, 1);
        nytClient.Setup(c => c.DownloadBonusPuzzle(date)).ReturnsAsync([1]);

        await CreateService().DeliverBonusPuzzle(date);

        remarkableClient.Verify(c => c.Upload(
            It.Is<string>(t => t.Contains("June") && t.Contains("2025")),
            It.IsAny<byte[]>()),
            Times.Once);
    }
}
