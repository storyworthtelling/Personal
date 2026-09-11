using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Foreword.Domain.Weather;
using Foreword.Infrastructure.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace Foreword.Tests.Pdf;

public class DayPdfComposerTests
{
    private readonly DayPdfComposer composer = new();
    private static readonly DateOnly Today = new(2026, 6, 1);

    private static int PageCount(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return PdfReader.Open(stream, PdfDocumentOpenMode.InformationOnly).PageCount;
    }

    private static CalendarEvent TimedEvent(PersonColumn column, int startHour, int endHour,
        string title, string? location = null) =>
        new()
        {
            Title = title,
            Location = location,
            Columns = [column],
            Start = Today.ToDateTime(new TimeOnly(startHour, 0)),
            End = Today.ToDateTime(new TimeOnly(endHour, 0))
        };

    /// <summary>
    /// An event strip is tall enough for its own title.
    ///
    /// <para>The literal it replaced was 11 points, fitted to an 8-point face. The registered ladder
    /// puts the event face at 9.75, whose line is taller than that — so the old constant would have
    /// clipped every title on the busiest page in the app, and clipped it silently: the page still
    /// renders, still paginates, and still passes a test that counts pages.</para>
    /// </summary>
    [Fact]
    public void AnEventStripIsTallerThanTheTextInIt()
    {
        var font = SkiaPageLayout.Sans(Storyworthtelling.Paper.PaperTypeScale.Label);

        var line = SkiaPageLayout.LineHeight(font);
        var strip = DayPdfComposer.StripHeight(font);

        Assert.True(strip > line, $"A {strip}pt strip cannot hold a {line}pt line.");
        Assert.True(strip > 11, $"The old fixed height was 11pt and the text now needs {line}pt.");
    }

    /// <summary>
    /// An unreachable drive is FLAGGED, not merely drawn. This asserts the page renders at all with
    /// travel on it — the band is geometry, so the honest end of this test is the eye, and it is in
    /// the on-device document. What it does catch is a band that throws or paginates the page away.
    /// </summary>
    [Fact]
    public void ADriveDrawsWithoutDisturbingThePage()
    {
        var soccer = TimedEvent(PersonColumn.Jordan, 16, 17, "Soccer practice", "Veterans Park");

        var agenda = new DayAgenda
        {
            Date = Today,
            Events = [TimedEvent(PersonColumn.Jordan, 15, 16, "Vet"), soccer],
            Travel = TravelTable.Of("Home", [("Home", "Veterans Park", 25)]),
        };

        var pdf = composer.Compose(new DayDocument { Date = agenda.Date, Agenda = agenda });

        Assert.Equal(4, PageCount(pdf));
        using var document = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(pdf));
        Assert.Contains("Soccer practice", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// B2's other half, on the page: a fourth event in the same hour is written under the grid
    /// rather than squeezed into a box a few characters wide. The owner asked for all of them.
    /// </summary>
    [Fact]
    public void AnEventPastTheLegibleWidthIsWrittenUnderTheGrid()
    {
        var agenda = new DayAgenda
        {
            Date = Today,
            Events =
            [
                TimedEvent(PersonColumn.Rowan, 9, 10, "First"),
                TimedEvent(PersonColumn.Rowan, 9, 10, "Second"),
                TimedEvent(PersonColumn.Rowan, 9, 10, "Third"),
            ],
        };

        var pdf = composer.Compose(new DayDocument { Date = agenda.Date, Agenda = agenda });

        using var document = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(pdf));
        var page = document.GetPage(1).Text;

        Assert.Contains("Also today", page, StringComparison.Ordinal);
        Assert.Contains("Third", page, StringComparison.Ordinal);
    }

    /// <summary>Nothing overflows on an ordinary day, so the page says nothing about it.</summary>
    [Fact]
    public void AnOrdinaryDayHasNoAlsoTodayList()
    {
        var agenda = new DayAgenda
        {
            Date = Today,
            Events = [TimedEvent(PersonColumn.Rowan, 9, 10, "Standup")],
        };

        var pdf = composer.Compose(new DayDocument { Date = agenda.Date, Agenda = agenda });

        using var document = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(pdf));
        Assert.DoesNotContain("Also today", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// D34, at the point it actually matters: a held calendar is NAMED on the page. Holding it
    /// silently would just be the old bug with the events missing instead of misfiled.
    /// </summary>
    [Fact]
    public void AHeldCalendarIsNamedOnThePage()
    {
        var agenda = new DayAgenda
        {
            Date = Today,
            Events = [],
            HeldCalendars = ["scouts@group"],
        };

        var pdf = composer.Compose(new DayDocument { Date = agenda.Date, Agenda = agenda });

        using var document = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(pdf));
        Assert.Contains("scouts@group", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    /// <summary>Nothing held, nothing said: the page carries no note about calendars.</summary>
    [Fact]
    public void NothingIsSaidWhenNothingIsHeld()
    {
        var agenda = new DayAgenda { Date = Today, Events = [] };

        var pdf = composer.Compose(new DayDocument { Date = agenda.Date, Agenda = agenda });

        using var document = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(pdf));
        Assert.DoesNotContain("no column yet", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_ProducesTwoPages_TimelineThenOutlook()
    {
        var agenda = new DayAgenda
        {
            Date = Today,
            Events = [TimedEvent(PersonColumn.Rowan, 9, 10, "Standup")]
        };
        var outlook = new[] { TimedEvent(PersonColumn.Jordan, 12, 13, "Dentist") };

        var pdf = composer.Compose(new DayDocument { Date = agenda.Date, Agenda = agenda, Outlook = outlook });

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal(4, PageCount(pdf));
    }

    [Fact]
    public void Compose_HandlesEmptyDay()
    {
        var agenda = new DayAgenda { Date = Today, Events = [] };

        var pdf = composer.Compose(new DayDocument { Date = agenda.Date, Agenda = agenda });

        Assert.True(pdf.Length > 0);
        Assert.Equal(4, PageCount(pdf));
    }

    [Fact]
    public void Compose_WithCarryoverNotes_DoesNotThrow()
    {
        var agenda = new DayAgenda { Date = Today, Events = [] };
        var carryovers = new[] { new ParsedNote { Text = "illegible scribble", Confidence = 0.2 } };

        var pdf = composer.Compose(new DayDocument { Date = agenda.Date, Agenda = agenda, UnreadableCarryovers = carryovers });

        Assert.True(PageCount(pdf) >= 2);
    }

    [Fact]
    public void Compose_RendersEventsAcrossAllColumns()
    {
        var agenda = new DayAgenda
        {
            Date = Today,
            Events =
            [
                TimedEvent(PersonColumn.Rowan, 8, 16, "Work"),
                TimedEvent(PersonColumn.Jordan, 8, 15, "Work"),
                TimedEvent(PersonColumn.Wren, 9, 12, "School"),
                TimedEvent(PersonColumn.Linden, 13, 14, "Nap"),
                TimedEvent(PersonColumn.Watch, 19, 22, "Red Sox")
            ]
        };

        var pdf = composer.Compose(new DayDocument { Date = agenda.Date, Agenda = agenda });

        Assert.Equal(4, PageCount(pdf));
    }

    // ---- the merged document ----

    /// <summary>
    /// Four pages, always, in the bound order — even on a day with nothing on it. A document whose
    /// page count depends on the day is one whose tabs point at different pages each morning.
    /// </summary>
    [Fact]
    public void TheDocumentIsAlwaysItsFourPages()
    {
        var empty = composer.Compose(new DayDocument
        {
            Date = Today,
            Agenda = new DayAgenda { Date = Today, Events = [] },
        });

        var full = composer.Compose(new DayDocument
        {
            Date = Today,
            Agenda = new DayAgenda { Date = Today, Events = [TimedEvent(PersonColumn.Rowan, 9, 10, "Standup")] },
            Outlook = [TimedEvent(PersonColumn.Jordan, 12, 13, "Dentist")],
            Tasks = [new TodoItem { Title = "Call Mom" }],
            Weather = Forecast(),
        });

        Assert.Equal(4, PageCount(empty));
        Assert.Equal(4, PageCount(full));
    }

    /// <summary>Every page carries all four tabs, so the strip is the same wherever you are.</summary>
    [Fact]
    public void EveryPageCarriesTheWholeTabStrip()
    {
        var pdf = composer.Compose(new DayDocument
        {
            Date = Today,
            Agenda = new DayAgenda { Date = Today, Events = [] },
            Weather = Forecast(),
        });

        using var document = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(pdf));

        foreach (var page in document.GetPages())
        {
            Assert.Equal(4, page.GetAnnotations()
                .Count(a => a.Type == UglyToad.PdfPig.Annotations.AnnotationType.Link));
        }
    }

    /// <summary>
    /// The pages say what they are. Read back as text rather than looked at, which is all a test can
    /// honestly claim about a drawn page — but it does catch a page composed into the wrong slot,
    /// which is the mistake that makes every tab in the document point somewhere unexpected.
    /// </summary>
    [Fact]
    public void ThePagesAreInTheBoundOrder()
    {
        var pdf = composer.Compose(new DayDocument
        {
            Date = Today,
            Agenda = new DayAgenda { Date = Today, Events = [] },
            Weather = Forecast(),
            Tasks = [new TodoItem { Title = "Call Mom" }],
        });

        using var document = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(pdf));

        Assert.Contains("Monday, June 1", document.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("Weather", document.GetPage(2).Text, StringComparison.Ordinal);
        Assert.Contains("The week ahead", document.GetPage(3).Text, StringComparison.Ordinal);
        Assert.Contains("Checklist", document.GetPage(4).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A task reaches the page it belongs on. The band is inferred, so this is the one place the
    /// inference and the drawing meet.
    /// </summary>
    [Fact]
    public void TasksAreDrawnUnderTheirBands()
    {
        var pdf = composer.Compose(new DayDocument
        {
            Date = Today,
            Agenda = new DayAgenda { Date = Today, Events = [] },
            Tasks =
            [
                new TodoItem { Title = "Vitamins" },
                new TodoItem { Title = "Call Mom" },
                new TodoItem { Title = "Bins out" },
            ],
        });

        using var document = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(pdf));
        var checklist = document.GetPage(4).Text;

        Assert.Contains("Vitamins", checklist, StringComparison.Ordinal);
        Assert.Contains("Call Mom", checklist, StringComparison.Ordinal);
        Assert.Contains("Bins out", checklist, StringComparison.Ordinal);
        Assert.Contains("MORNING", checklist, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EVENING", checklist, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A morning with no forecast says so rather than drawing an empty chart. An empty chart is a
    /// forecast of nothing, which is a different and worse claim than "no forecast came back".
    /// </summary>
    [Fact]
    public void AMorningWithNoForecastSaysSo()
    {
        var pdf = composer.Compose(new DayDocument
        {
            Date = Today,
            Agenda = new DayAgenda { Date = Today, Events = [] },
        });

        using var document = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(pdf));

        Assert.Contains("No forecast", document.GetPage(2).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The owner's line is drawn when there is one. Nothing is drawn when there is not — a neutral
    /// sentence in the script face would be the app impersonating them.
    /// </summary>
    [Fact]
    public void TheVoiceIsDrawnOnlyWhenThereIsOne()
    {
        var agenda = new DayAgenda { Date = Today, Events = [] };

        var withVoice = composer.Compose(new DayDocument
        {
            Date = Today, Agenda = agenda, Voice = "Otherwise a quiet Sunday.",
        });
        var without = composer.Compose(new DayDocument { Date = Today, Agenda = agenda });

        using var spoken = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(withVoice));
        using var silent = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(without));

        Assert.Contains("quiet Sunday", spoken.GetPage(1).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("quiet Sunday", silent.GetPage(1).Text, StringComparison.Ordinal);
    }

    private static WeatherReport Forecast() => new()
    {
        Today = new TodayWeather
        {
            Date = Today,
            High = 78,
            Low = 57,
            HighAt = Today.ToDateTime(new TimeOnly(15, 0)),
            Condition = "Clearing",
            Sun = new SunAndMoon(new TimeOnly(6, 32), new TimeOnly(19, 21), "WANING_GIBBOUS"),
            Hours =
            [
                .. Enumerable.Range(0, 17).Select(i => new HourWeather(
                    Today.ToDateTime(new TimeOnly(6 + i, 0)),
                    57 + i,
                    i < 4 ? 70 : 0)),
            ],
        },
        Week =
        [
            .. Enumerable.Range(0, 7).Select(i =>
                new WeekDay(Today.AddDays(i), "Sunny", 78 + i, 57 + i, 5)),
        ],
        Allergy =
        [
            new AllergyReading("Ragweed", "High", 4),
            new AllergyReading("Grass", "Low", 1),
            new AllergyReading("Tree", "None", 0),
            new AllergyReading("Air quality", "50 · Moderate", 2),
        ],
    };
}
