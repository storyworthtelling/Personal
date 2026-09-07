using Foreword.Domain.Models;
using Foreword.Infrastructure.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace Foreword.Tests.Pdf;

public class CalendarPdfComposerTests
{
    private readonly CalendarPdfComposer composer = new();
    private static readonly DateOnly Today = new(2026, 6, 1);

    private static int PageCount(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return PdfReader.Open(stream, PdfDocumentOpenMode.InformationOnly).PageCount;
    }

    private static CalendarEvent TimedEvent(PersonColumn column, int startHour, int endHour, string title) =>
        new()
        {
            Title = title,
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
        var strip = CalendarPdfComposer.StripHeight(font);

        Assert.True(strip > line, $"A {strip}pt strip cannot hold a {line}pt line.");
        Assert.True(strip > 11, $"The old fixed height was 11pt and the text now needs {line}pt.");
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

        var pdf = composer.Compose(agenda, [], []);

        using var document = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(pdf));
        Assert.Contains("scouts@group", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    /// <summary>Nothing held, nothing said: the page carries no note about calendars.</summary>
    [Fact]
    public void NothingIsSaidWhenNothingIsHeld()
    {
        var agenda = new DayAgenda { Date = Today, Events = [] };

        var pdf = composer.Compose(agenda, [], []);

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

        var pdf = composer.Compose(agenda, outlook, []);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal(2, PageCount(pdf));
    }

    [Fact]
    public void Compose_HandlesEmptyDay()
    {
        var agenda = new DayAgenda { Date = Today, Events = [] };

        var pdf = composer.Compose(agenda, [], []);

        Assert.True(pdf.Length > 0);
        Assert.Equal(2, PageCount(pdf));
    }

    [Fact]
    public void Compose_WithCarryoverNotes_DoesNotThrow()
    {
        var agenda = new DayAgenda { Date = Today, Events = [] };
        var carryovers = new[] { new ParsedNote { Text = "illegible scribble", Confidence = 0.2 } };

        var pdf = composer.Compose(agenda, [], carryovers);

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

        var pdf = composer.Compose(agenda, [], []);

        Assert.Equal(2, PageCount(pdf));
    }
}
