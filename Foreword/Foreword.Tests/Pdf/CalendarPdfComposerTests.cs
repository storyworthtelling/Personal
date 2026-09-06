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
            Column = column,
            Start = Today.ToDateTime(new TimeOnly(startHour, 0)),
            End = Today.ToDateTime(new TimeOnly(endHour, 0))
        };

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
