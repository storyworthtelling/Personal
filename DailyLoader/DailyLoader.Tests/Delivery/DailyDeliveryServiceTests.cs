using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using DailyLoader.Domain.Interfaces;
using DailyLoader.Domain.Models;
using DailyLoader.Infrastructure.Calendar;
using DailyLoader.Infrastructure.Delivery;
using DailyLoader.Infrastructure.Google;
using DailyLoader.Infrastructure.Handwriting;
using Storyworthtelling.Remarkable;
using Xunit;

namespace DailyLoader.Tests.Delivery;

public class DailyDeliveryServiceTests
{
    private static readonly DateOnly Date = new(2026, 6, 2);
    private const string YesterdayCalendar = "Daily Calendar 2026-06-01";
    private const string YesterdayChecklist = "Daily Checklist 2026-06-01";

    private readonly Mock<IGoogleCalendarClient> calendar = new();
    private readonly Mock<IGoogleTasksClient> tasks = new();
    private readonly Mock<ICalendarPdfComposer> calendarComposer = new();
    private readonly Mock<IChecklistPdfComposer> checklistComposer = new();
    private readonly Mock<IHandwritingExtractor> handwriting = new();
    private readonly Mock<IRemarkableClient> remarkable = new();

    public DailyDeliveryServiceTests()
    {
        calendar.Setup(c => c.GetEventsForDay(It.IsAny<DateOnly>())).ReturnsAsync([]);
        calendar.Setup(c => c.GetEventsInRange(It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync([]);
        tasks.Setup(t => t.GetMyTasks()).ReturnsAsync([]);
        calendarComposer
            .Setup(c => c.Compose(It.IsAny<DayAgenda>(), It.IsAny<IReadOnlyList<CalendarEvent>>(), It.IsAny<IReadOnlyList<ParsedNote>>()))
            .Returns([1]);
        checklistComposer
            .Setup(c => c.Compose(It.IsAny<IReadOnlyList<TodoItem>>(), It.IsAny<DateOnly>(), It.IsAny<IReadOnlyList<ParsedNote>>()))
            .Returns([2]);
        // No prior documents by default.
        remarkable.Setup(r => r.DownloadByTitle(It.IsAny<string>())).ReturnsAsync((RemarkableDoc?)null);
    }

    private DailyDeliveryService CreateService() =>
        new(calendar.Object, tasks.Object,
            new CalendarColumnMapper(Options.Create(new CalendarMappingOptions())),
            new WorkScheduleAugmenter(Options.Create(new WorkScheduleOptions())),
            new OutlookSelector(),
            calendarComposer.Object, checklistComposer.Object,
            handwriting.Object, remarkable.Object,
            Options.Create(new AnthropicOptions { ConfidenceThreshold = 0.6 }),
            Options.Create(new GoogleOptions()),
            NullLogger<DailyDeliveryService>.Instance);

    private void SetupYesterdayDoc(string title, string id, IReadOnlyList<ParsedNote> notes)
    {
        var doc = new RemarkableDoc { Id = id, Title = title, PdfBytes = [37, 80, 68, 70] };
        remarkable.Setup(r => r.DownloadByTitle(title)).ReturnsAsync(doc);
        handwriting.Setup(h => h.Extract(doc.PdfBytes)).ReturnsAsync(notes);
    }

    [Fact]
    public async Task UploadsCalendarAndChecklist()
    {
        await CreateService().DeliverDailyDocuments(Date);

        remarkable.Verify(r => r.Upload("Daily Calendar 2026-06-02", It.IsAny<byte[]>()), Times.Once);
        remarkable.Verify(r => r.Upload("Daily Checklist 2026-06-02", It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public async Task HighConfidenceEventNote_IsAddedToCalendar()
    {
        SetupYesterdayDoc(YesterdayCalendar, "cal-y",
            [new ParsedNote { Text = "Linden recital", When = new DateTime(2026, 6, 10, 18, 0, 0), Column = PersonColumn.Linden, IsTask = false, Confidence = 0.95 }]);

        await CreateService().DeliverDailyDocuments(Date);

        calendar.Verify(c => c.AddEvent(It.Is<CalendarEvent>(e => e.Title == "Linden recital" && e.Column == PersonColumn.Linden)), Times.Once);
    }

    [Fact]
    public async Task HighConfidenceTaskNote_IsAddedToTasks()
    {
        SetupYesterdayDoc(YesterdayChecklist, "chk-y",
            [new ParsedNote { Text = "renew passport", IsTask = true, Confidence = 0.9 }]);

        await CreateService().DeliverDailyDocuments(Date);

        tasks.Verify(t => t.AddTask("renew passport", It.IsAny<DateOnly?>()), Times.Once);
    }

    [Fact]
    public async Task LowConfidenceNote_IsCarriedOverNotAdded()
    {
        SetupYesterdayDoc(YesterdayCalendar, "cal-y",
            [new ParsedNote { Text = "illegible", IsTask = false, Confidence = 0.2 }]);

        IReadOnlyList<ParsedNote>? captured = null;
        calendarComposer
            .Setup(c => c.Compose(It.IsAny<DayAgenda>(), It.IsAny<IReadOnlyList<CalendarEvent>>(), It.IsAny<IReadOnlyList<ParsedNote>>()))
            .Callback<DayAgenda, IReadOnlyList<CalendarEvent>, IReadOnlyList<ParsedNote>>((_, _, notes) => captured = notes)
            .Returns([1]);

        await CreateService().DeliverDailyDocuments(Date);

        calendar.Verify(c => c.AddEvent(It.IsAny<CalendarEvent>()), Times.Never);
        Assert.NotNull(captured);
        Assert.Contains(captured!, n => n.Text == "illegible");
    }

    [Fact]
    public async Task TrashesYesterdayDocuments_WhenBothUploadsSucceed()
    {
        SetupYesterdayDoc(YesterdayCalendar, "cal-y", []);
        SetupYesterdayDoc(YesterdayChecklist, "chk-y", []);

        await CreateService().DeliverDailyDocuments(Date);

        remarkable.Verify(r => r.MoveToTrash("cal-y"), Times.Once);
        remarkable.Verify(r => r.MoveToTrash("chk-y"), Times.Once);
    }

    [Fact]
    public async Task DoesNotTrash_WhenAnUploadFails_ButStillAttemptsBothUploads()
    {
        SetupYesterdayDoc(YesterdayCalendar, "cal-y", []);
        remarkable.Setup(r => r.Upload("Daily Calendar 2026-06-02", It.IsAny<byte[]>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        await CreateService().DeliverDailyDocuments(Date);

        // The checklist upload is still attempted despite the calendar upload failing.
        remarkable.Verify(r => r.Upload("Daily Checklist 2026-06-02", It.IsAny<byte[]>()), Times.Once);
        remarkable.Verify(r => r.MoveToTrash(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ReadBackFailure_DoesNotBlockDelivery()
    {
        remarkable.Setup(r => r.DownloadByTitle(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("read path broken"));

        await CreateService().DeliverDailyDocuments(Date);

        remarkable.Verify(r => r.Upload("Daily Calendar 2026-06-02", It.IsAny<byte[]>()), Times.Once);
        remarkable.Verify(r => r.Upload("Daily Checklist 2026-06-02", It.IsAny<byte[]>()), Times.Once);
    }
}
