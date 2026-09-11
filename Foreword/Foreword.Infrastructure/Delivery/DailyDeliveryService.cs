using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Calendar;
using Foreword.Domain.Document;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;
using Foreword.Infrastructure.Handwriting;
using Foreword.Infrastructure.Google;
using Storyworthtelling.Remarkable;

namespace Foreword.Infrastructure.Delivery;

// Orchestrates the morning delivery: reads back yesterday's hand-written notes, builds today's
// agenda, outlook, tasks, weather and travel, uploads ONE four-page document, and trashes
// yesterday's once today's upload succeeds.
public class DailyDeliveryService(
    IGoogleCalendarClient googleCalendar,
    IGoogleTasksClient googleTasks,
    CalendarColumnMapper columnMapper,
    WorkScheduleAugmenter workAugmenter,
    IOutlookSelector outlookSelector,
    IDayPdfComposer dayComposer,
    IWeatherClient weather,
    IRouteClient routes,
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
        agenda.Travel = await MeasureTravel(agenda);
        var outlook = await BuildOutlook(date);
        var tasks = await googleTasks.GetMyTasks();

        var document = new DayDocument
        {
            Date = date,
            Agenda = agenda,
            Outlook = outlook,
            Tasks = tasks,
            Weather = await ReadWeather(date),
            UnreadableCarryovers = carryovers,
        };

        if (await TryUpload(DayTitles.Document(date), dayComposer.Compose(document)))
            await TrashDocs(yesterdayDocIds);
        else
            logger.LogWarning("Skipping trash of yesterday's documents because the upload failed for {Date}", date);
    }

    // Creates the Wren and Linden calendars if they do not yet exist. Called once at startup.
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
        var events = await googleCalendar.GetEventsForDay(date);

        // An unmapped calendar is held rather than printed into the default column (D34), and named
        // on the page. Held BEFORE the columns are assigned: a calendar nobody has mapped has no
        // column to be assigned to, and the old fallback is exactly the bug.
        // A calendar the owner has answered with "not printed" is dropped SILENTLY. That is the
        // difference between it and an unmapped one: this has been looked at and decided, so saying
        // so every morning would be nagging about a settled question.
        var wanted = events.Where(e => !columnMapper.IsNotPrinted(e.SourceCalendarId)).ToList();

        var split = HeldCalendars.Split(wanted, columnMapper.KnownCalendars());
        if (split.Held.Count > 0)
        {
            logger.LogInformation(
                "Holding {Count} unmapped calendar(s): {Calendars}",
                split.Held.Count, string.Join(", ", split.Held));
        }

        var augmented = workAugmenter.Augment(columnMapper.Assign(split.Printed), date);
        return new DayAgenda { Date = date, Events = augmented, HeldCalendars = split.Held };
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

        // Best-effort: a failure here (e.g. the reMarkable read path) must not block today's
        // document. Every title yesterday's sheets might be under, including the two the old
        // build used — see DayTitles.ToReadBack, which is what stops the merge silently
        // abandoning whatever was written on the morning it ships.
        foreach (var title in DayTitles.ToReadBack(yesterday))
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
            Columns = [note.Column ?? PersonColumn.Rowan],
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

    /// <summary>
    /// The weather, or nothing. Four calls behind one seam, none of which is worth the morning: a
    /// document with no weather page content still carries the day, and the day is the point.
    /// </summary>
    private async Task<Domain.Weather.WeatherReport> ReadWeather(DateOnly date)
    {
        try
        {
            return await weather.GetReport(date);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Weather read failed for {Date}", date);
            return Domain.Weather.WeatherReport.Nothing;
        }
    }

    /// <summary>
    /// The drives today's columns actually need, measured once.
    ///
    /// <para>TravelPlan works out which legs a person's day requires; this asks the router for
    /// exactly those. Every column is walked separately, because a drive starts where THAT person's
    /// day has left them — the parent coming in from work and the child coming out of school are
    /// making two different journeys to the same field.</para>
    /// </summary>
    private async Task<TravelTable> MeasureTravel(DayAgenda agenda)
    {
        try
        {
            var home = TravelTable.NothingMeasured.Home;
            var legs = new List<(string From, string To)>();

            foreach (var column in ColumnSet.People)
            {
                var day = agenda.Events.Where(e => e.Columns.Contains(column)).ToList();

                // Asked with no route table, so every located event reports the leg it would need.
                foreach (var travel in TravelPlan.For(day, home, (_, _) => 1))
                {
                    legs.Add((travel.From, travel.Event.Location!.Trim()));
                }
            }

            if (legs.Count == 0)
            {
                return TravelTable.NothingMeasured;
            }

            var measured = await routes.Measure(legs, agenda.Date.ToDateTime(TimeOnly.MinValue));
            return measured.AsTable(home);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Travel times could not be measured for {Date}", agenda.Date);
            return TravelTable.NothingMeasured;
        }
    }
}
