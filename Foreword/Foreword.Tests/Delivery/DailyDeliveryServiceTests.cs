using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Foreword.Domain.Calendar;
using Foreword.Domain.Document;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Weather;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;
using Foreword.Infrastructure.Delivery;
using Foreword.Infrastructure.Google;
using Foreword.Infrastructure.Handwriting;
using Storyworthtelling.Remarkable;
using Xunit;

namespace Foreword.Tests.Delivery;

public class DailyDeliveryServiceTests
{
    private static readonly DateOnly Date = new(2026, 6, 2);
    private const string YesterdayCalendar = "Daily Calendar 2026-06-01";
    private const string YesterdayChecklist = "Daily Checklist 2026-06-01";

    private readonly Mock<IGoogleCalendarClient> calendar = new();
    private readonly Mock<IGoogleTasksClient> tasks = new();
    private readonly Mock<IDayPdfComposer> dayComposer = new();
    private readonly Mock<IWeatherClient> weather = new();
    private readonly Mock<IRouteClient> routes = new();
    private readonly Mock<IHandwritingExtractor> handwriting = new();
    private readonly Mock<IRemarkableClient> remarkable = new();

    public DailyDeliveryServiceTests()
    {
        calendar.Setup(c => c.GetEventsForDay(It.IsAny<DateOnly>())).ReturnsAsync([]);
        calendar.Setup(c => c.GetEventsInRange(It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync([]);
        tasks.Setup(t => t.GetMyTasks()).ReturnsAsync([]);
        dayComposer.Setup(c => c.Compose(It.IsAny<DayDocument>())).Returns([1]);
        weather.Setup(w => w.GetReport(It.IsAny<DateOnly>())).ReturnsAsync(WeatherReport.Nothing);
        routes.Setup(r => r.Measure(
                It.IsAny<IReadOnlyList<(string, string)>>(), It.IsAny<DateTime>()))
            .ReturnsAsync(TravelLegs.None);
        // No prior documents by default.
        remarkable.Setup(r => r.DownloadByTitle(It.IsAny<string>())).ReturnsAsync((RemarkableDoc?)null);
    }

    private DailyDeliveryService CreateService() =>
        new(calendar.Object, tasks.Object,
            new CalendarColumnMapper(Options.Create(new CalendarMappingOptions())),
            new WorkScheduleAugmenter(Options.Create(new WorkScheduleOptions())),
            new OutlookSelector(),
            dayComposer.Object, weather.Object, routes.Object,
            handwriting.Object, remarkable.Object,
            Options.Create(new AnthropicOptions { ConfidenceThreshold = 0.6 }),
            Options.Create(new GoogleOptions()),
            NullLogger<DailyDeliveryService>.Instance);

    private void SetupYesterdayDoc(string title, string id, IReadOnlyList<ParsedNote> notes)
    {
        // Distinct bytes per document. Moq matches a byte[] argument STRUCTURALLY, so two documents
        // with the same content would make the second Extract setup shadow the first — and a test
        // that set up two sheets would silently only ever read one of them.
        var doc = new RemarkableDoc { Id = id, Title = title, PdfBytes = [37, 80, 68, 70, .. System.Text.Encoding.ASCII.GetBytes(id)] };
        remarkable.Setup(r => r.DownloadByTitle(title)).ReturnsAsync(doc);
        handwriting.Setup(h => h.Extract(doc.PdfBytes)).ReturnsAsync(notes);
    }

    /// <summary>One document now, not two. The whole point of the merge.</summary>
    [Fact]
    public async Task UploadsOneDocument()
    {
        await CreateService().DeliverDailyDocuments(Date);

        remarkable.Verify(r => r.Upload("The Day 2026-06-02", It.IsAny<byte[]>()), Times.Once);
        remarkable.Verify(r => r.Upload(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Once);
    }

    /// <summary>
    /// The morning the merge ships, what is on the tablet is yesterday's calendar and yesterday's
    /// checklist under the OLD names. A read-back that only looked for the new name would find
    /// nothing, file nothing of what was written on them, and trash nothing — silently, on the one
    /// morning the owner would least expect the app to have changed its mind about what it is
    /// called.
    /// </summary>
    [Fact]
    public async Task StillReadsBackWhatTheOldBuildLeftOnTheTablet()
    {
        SetupYesterdayDoc(YesterdayCalendar, "cal-y",
            [new ParsedNote { Text = "Eva recital", When = new DateTime(2026, 6, 10, 18, 0, 0), Column = PersonColumn.Eva, IsTask = false, Confidence = 0.95 }]);
        SetupYesterdayDoc(YesterdayChecklist, "chk-y",
            [new ParsedNote { Text = "renew passport", IsTask = true, Confidence = 0.9 }]);

        await CreateService().DeliverDailyDocuments(Date);

        calendar.Verify(c => c.AddEvent(It.Is<CalendarEvent>(e => e.Title == "Eva recital")), Times.Once);
        tasks.Verify(t => t.AddTask("renew passport", It.IsAny<DateOnly?>()), Times.Once);
        remarkable.Verify(r => r.MoveToTrash("cal-y"), Times.Once);
        remarkable.Verify(r => r.MoveToTrash("chk-y"), Times.Once);
    }

    /// <summary>
    /// And the day after, when yesterday's is a merged document under the new name.
    /// </summary>
    [Fact]
    public async Task ReadsBackYesterdaysMergedDocument()
    {
        SetupYesterdayDoc("The Day 2026-06-01", "day-y",
            [new ParsedNote { Text = "renew passport", IsTask = true, Confidence = 0.9 }]);

        await CreateService().DeliverDailyDocuments(Date);

        tasks.Verify(t => t.AddTask("renew passport", It.IsAny<DateOnly?>()), Times.Once);
        remarkable.Verify(r => r.MoveToTrash("day-y"), Times.Once);
    }

    /// <summary>
    /// A weather service that falls over does not cost the morning. The day is the point; the
    /// forecast is a page of it.
    /// </summary>
    [Fact]
    public async Task AFailedWeatherCallStillDelivers()
    {
        weather.Setup(w => w.GetReport(It.IsAny<DateOnly>()))
            .ThrowsAsync(new HttpRequestException("weather is down"));

        await CreateService().DeliverDailyDocuments(Date);

        remarkable.Verify(r => r.Upload("The Day 2026-06-02", It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public async Task AFailedRouteCallStillDelivers()
    {
        routes.Setup(r => r.Measure(It.IsAny<IReadOnlyList<(string, string)>>(), It.IsAny<DateTime>()))
            .ThrowsAsync(new HttpRequestException("routes is down"));

        await CreateService().DeliverDailyDocuments(Date);

        remarkable.Verify(r => r.Upload("The Day 2026-06-02", It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public async Task HighConfidenceEventNote_IsAddedToCalendar()
    {
        SetupYesterdayDoc(YesterdayCalendar, "cal-y",
            [new ParsedNote { Text = "Eva recital", When = new DateTime(2026, 6, 10, 18, 0, 0), Column = PersonColumn.Eva, IsTask = false, Confidence = 0.95 }]);

        await CreateService().DeliverDailyDocuments(Date);

        calendar.Verify(c => c.AddEvent(It.Is<CalendarEvent>(e => e.Title == "Eva recital" && e.Columns.Contains(PersonColumn.Eva))), Times.Once);
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
        dayComposer.Setup(c => c.Compose(It.IsAny<DayDocument>()))
            .Callback<DayDocument>(document => captured = document.UnreadableCarryovers)
            .Returns([1]);

        await CreateService().DeliverDailyDocuments(Date);

        calendar.Verify(c => c.AddEvent(It.IsAny<CalendarEvent>()), Times.Never);
        Assert.NotNull(captured);
        Assert.Contains(captured!, n => n.Text == "illegible");
    }

    [Fact]
    public async Task TrashesYesterdayDocuments_WhenTheUploadSucceeds()
    {
        SetupYesterdayDoc(YesterdayCalendar, "cal-y", []);
        SetupYesterdayDoc(YesterdayChecklist, "chk-y", []);

        await CreateService().DeliverDailyDocuments(Date);

        remarkable.Verify(r => r.MoveToTrash("cal-y"), Times.Once);
        remarkable.Verify(r => r.MoveToTrash("chk-y"), Times.Once);
    }

    /// <summary>
    /// Yesterday's sheets stay put when today's does not land. They are the only copy of whatever
    /// was written on them, and something the app could not read this morning it can read
    /// tomorrow — trashing them on a failed upload would throw away the morning twice.
    /// </summary>
    [Fact]
    public async Task DoesNotTrash_WhenTheUploadFails()
    {
        SetupYesterdayDoc(YesterdayCalendar, "cal-y", []);
        remarkable.Setup(r => r.Upload("The Day 2026-06-02", It.IsAny<byte[]>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        await CreateService().DeliverDailyDocuments(Date);

        remarkable.Verify(r => r.MoveToTrash(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ReadBackFailure_DoesNotBlockDelivery()
    {
        remarkable.Setup(r => r.DownloadByTitle(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("read path broken"));

        await CreateService().DeliverDailyDocuments(Date);

        remarkable.Verify(r => r.Upload("The Day 2026-06-02", It.IsAny<byte[]>()), Times.Once);
    }
}
