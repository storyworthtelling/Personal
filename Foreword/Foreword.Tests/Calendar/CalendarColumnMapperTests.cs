using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;
using Microsoft.Extensions.Options;
using Xunit;

namespace Foreword.Tests.Calendar;

public class CalendarColumnMapperTests
{
    private static CalendarColumnMapper CreateMapper(CalendarMappingOptions options) =>
        new(Options.Create(options));

    private static CalendarEvent Event(string calendarId, string title = "Thing") =>
        new() { Title = title, SourceCalendarId = calendarId };

    [Fact]
    public void Resolve_UsesExplicitCalendarMapping()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            CalendarColumns = { ["ashley-work@group"] = PersonColumn.Ashley }
        });

        Assert.Equal([PersonColumn.Ashley], mapper.Resolve(Event("ashley-work@group")));
    }

    [Fact]
    public void Resolve_SportsCalendarMapsToWatch()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            CalendarColumns = { ["redsox@group"] = PersonColumn.Watch }
        });

        Assert.Equal([PersonColumn.Watch], mapper.Resolve(Event("redsox@group", "Red Sox vs Yankees")));
    }

    [Fact]
    public void Resolve_UnmappedCalendarFallsBackToDefaultColumn()
    {
        var mapper = CreateMapper(new CalendarMappingOptions { DefaultColumn = PersonColumn.Matt });

        Assert.Equal([PersonColumn.Matt], mapper.Resolve(Event("church@group")));
    }

    [Fact]
    public void Resolve_FamilyCalendarRoutesGwenByKeyword()
    {
        var mapper = CreateMapper(new CalendarMappingOptions { FamilyCalendarId = "family@group" });

        Assert.Equal([PersonColumn.Gwen], mapper.Resolve(Event("family@group", "Gwen dentist appt")));
    }

    [Fact]
    public void Resolve_FamilyCalendarRoutesEvaByKeyword()
    {
        var mapper = CreateMapper(new CalendarMappingOptions { FamilyCalendarId = "family@group" });

        Assert.Equal([PersonColumn.Eva], mapper.Resolve(Event("family@group", "Eva soccer practice")));
    }

    /// <summary>
    /// CHANGED BY D31, and this is the change stated plainly. An event on the shared calendar that
    /// names nobody used to fall to the default column — so "Family dinner" was Matt's, and only
    /// Matt's. Nobody named on a shared calendar now means EVERYBODY, which is what a calendar
    /// called Family means.
    /// </summary>
    [Fact]
    public void Resolve_SharedCalendarWithNobodyNamedMeansEverybody()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            FamilyCalendarId = "family@group",
            DefaultColumn = PersonColumn.Matt
        });

        Assert.Equal(ColumnSet.People, mapper.Resolve(Event("family@group", "Family dinner")));
    }

    /// <summary>Every name in the title, not the first — the bug D31 exists to fix.</summary>
    [Fact]
    public void Resolve_SharedCalendarSplitsOnEveryNameInTheTitle()
    {
        var mapper = CreateMapper(new CalendarMappingOptions { FamilyCalendarId = "family@group" });

        Assert.Equal(
            [PersonColumn.Gwen, PersonColumn.Eva],
            mapper.Resolve(Event("family@group", "Gwen and Eva to swimming")));
    }

    /// <summary>An invited address is a fact; it is read before the title (D31).</summary>
    [Fact]
    public void Resolve_SharedCalendarReadsTheGuestsBeforeTheTitle()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            FamilyCalendarId = "family@group",
            PersonAddresses = { [PersonColumn.Eva] = ["eva@example.com"] },
        });

        var invited = new CalendarEvent
        {
            Title = "Gwen to swimming",
            SourceCalendarId = "family@group",
            Guests = ["eva@example.com"],
        };

        Assert.Equal([PersonColumn.Eva], mapper.Resolve(invited));
    }

    [Fact]
    public void Assign_SetsColumnOnEveryEvent()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            CalendarColumns = { ["w@group"] = PersonColumn.Ashley }
        });
        var events = new[] { Event("w@group"), Event("unmapped@group") };

        var assigned = mapper.Assign(events);

        Assert.Equal([PersonColumn.Ashley], assigned[0].Columns);
        Assert.Equal([PersonColumn.Matt], assigned[1].Columns);
    }
}
