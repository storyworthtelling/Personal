using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DailyLoader.Domain.Interfaces;
using DailyLoader.Domain.Models;
using DailyLoader.Infrastructure.Calendar;
using DailyLoader.Infrastructure.Handwriting;
using DailyLoader.Infrastructure.Google;

namespace DailyLoader.Infrastructure.Delivery;

// Orchestrates the daily calendar + checklist delivery: reads back yesterday's hand-written
// notes, builds today's agenda/outlook/tasks, uploads the two new documents, and trashes
// yesterday's once today's upload succeeds.
public class DailyDeliveryService(
    IGoogleCalendarClient googleCalendar,
    IGoogleTasksClient googleTasks,
    CalendarColumnMapper columnMapper,
    WorkScheduleAugmenter workAugmenter,
    IOutlookSelector outlookSelector,
    ICalendarPdfComposer calendarComposer,
    IChecklistPdfComposer checklistComposer,
    IHandwritingExtractor handwriting,
    IRemarkableClient remarkable,
    IOptions<AnthropicOptions> anthropicOptions,
    IOptions<GoogleOptions> googleOptions,
    ILogger<DailyDeliveryService> logger)
{
    private const int OutlookDays = 6; // today + 6 = a seven-day window

    public async Task DeliverDailyDocuments(DateOnly date)
    {
        var (carryovers, yesterdayDocIds) = await ReadBackYesterday(date);

        var agenda = await BuildAgenda(date);
        var outlook = await BuildOutlook(date);
        var tasks = await googleTasks.GetMyTasks();

        var calendarPdf = calendarComposer.Compose(agenda, outlook, carryovers);
        var checklistPdf = checklistComposer.Compose(tasks, date, carryovers);

        var calendarUploaded = await TryUpload(CalendarTitle(date), calendarPdf);
        var checklistUploaded = await TryUpload(ChecklistTitle(date), checklistPdf);

        if (calendarUploaded && checklistUploaded)
            await TrashDocs(yesterdayDocIds);
        else
            logger.LogWarning("Skipping trash of yesterday's documents because an upload failed for {Date}", date);
    }

    // Creates the Gwen and Eva calendars if they do not yet exist. Called once at startup.
    public async Task EnsureKidCalendars()
    {
        var options = googleOptions.Value;
        foreach (var name in new[] { options.GwenCalendarName, options.EvaCalendarName })
        {
            try
            {
                var id = await googleCalendar.EnsureCalendar(name);
                logger.LogInformation("Ensured calendar {Name} ({Id})", name, id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not ensure calendar {Name}", name);
            }
        }
    }

    private async Task<DayAgenda> BuildAgenda(DateOnly date)
    {
        var events = columnMapper.Assign(await googleCalendar.GetEventsForDay(date));
        var augmented = workAugmenter.Augment(events, date);
        return new DayAgenda { Date = date, Events = augmented };
    }

    private async Task<IReadOnlyList<CalendarEvent>> BuildOutlook(DateOnly date)
    {
        var events = columnMapper.Assign(await googleCalendar.GetEventsInRange(date, date.AddDays(OutlookDays)));
        workAugmenter.MarkOffDays(events);
        return outlookSelector.SelectNonRoutine(events);
    }

    private async Task<(IReadOnlyList<ParsedNote> Carryovers, IReadOnlyList<string> DocIds)> ReadBackYesterday(DateOnly date)
    {
        var yesterday = date.AddDays(-1);
        var carryovers = new List<ParsedNote>();
        var docIds = new List<string>();
        var threshold = anthropicOptions.Value.ConfidenceThreshold;

        // Best-effort: a failure here (e.g. the reMarkable read path) must not block today's docs.
        foreach (var title in new[] { CalendarTitle(yesterday), ChecklistTitle(yesterday) })
        {
            try
            {
                var doc = await remarkable.DownloadByTitle(title);
                if (doc is null)
                    continue;
                docIds.Add(doc.Id);

                foreach (var note in await handwriting.Extract(doc.PdfBytes))
                {
                    if (note.Confidence >= threshold)
                        await ApplyNote(note);
                    else
                        carryovers.Add(note);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Handwriting read-back failed for {Title}", title);
            }
        }

        return (carryovers, docIds);
    }

    private async Task ApplyNote(ParsedNote note)
    {
        if (note.IsTask)
        {
            await googleTasks.AddTask(note.Text, note.When is { } w ? DateOnly.FromDateTime(w) : null);
            logger.LogInformation("Added task from handwriting: {Text}", note.Text);
            return;
        }

        var start = note.When ?? DateTime.Today;
        var isAllDay = note.When is null || note.When.Value.TimeOfDay == TimeSpan.Zero;
        await googleCalendar.AddEvent(new CalendarEvent
        {
            Title = note.Text,
            Column = note.Column ?? PersonColumn.Matt,
            Start = start,
            End = isAllDay ? start.Date.AddDays(1) : start.AddHours(1),
            IsAllDay = isAllDay
        });
        logger.LogInformation("Added event from handwriting: {Text}", note.Text);
    }

    private async Task<bool> TryUpload(string title, byte[] pdf)
    {
        try
        {
            await remarkable.Upload(title, pdf);
            logger.LogInformation("Uploaded {Title}", title);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Upload failed for {Title}", title);
            return false;
        }
    }

    private async Task TrashDocs(IReadOnlyList<string> docIds)
    {
        foreach (var id in docIds)
        {
            try
            {
                await remarkable.MoveToTrash(id);
                logger.LogInformation("Moved previous document {Id} to trash", id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not trash previous document {Id}", id);
            }
        }
    }

    private static string CalendarTitle(DateOnly date) => $"Daily Calendar {date:yyyy-MM-dd}";
    private static string ChecklistTitle(DateOnly date) => $"Daily Checklist {date:yyyy-MM-dd}";
}
