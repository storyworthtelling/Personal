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
            CalendarColumns = { ["jordan-work@group"] = PersonColumn.Jordan }
        });

        Assert.Equal(PersonColumn.Jordan, mapper.Resolve(Event("jordan-work@group")));
    }

    [Fact]
    public void Resolve_SportsCalendarMapsToWatch()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            CalendarColumns = { ["redsox@group"] = PersonColumn.Watch }
        });

        Assert.Equal(PersonColumn.Watch, mapper.Resolve(Event("redsox@group", "Red Sox vs Yankees")));
    }

    [Fact]
    public void Resolve_UnmappedCalendarFallsBackToDefaultColumn()
    {
        var mapper = CreateMapper(new CalendarMappingOptions { DefaultColumn = PersonColumn.Rowan });

        Assert.Equal(PersonColumn.Rowan, mapper.Resolve(Event("church@group")));
    }

    [Fact]
    public void Resolve_FamilyCalendarRoutesGwenByKeyword()
    {
        var mapper = CreateMapper(new CalendarMappingOptions { FamilyCalendarId = "family@group" });

        Assert.Equal(PersonColumn.Wren, mapper.Resolve(Event("family@group", "Wren dentist appt")));
    }

    [Fact]
    public void Resolve_FamilyCalendarRoutesEvaByKeyword()
    {
        var mapper = CreateMapper(new CalendarMappingOptions { FamilyCalendarId = "family@group" });

        Assert.Equal(PersonColumn.Linden, mapper.Resolve(Event("family@group", "Linden soccer practice")));
    }

    [Fact]
    public void Resolve_FamilyCalendarWithoutKidKeywordUsesDefault()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            FamilyCalendarId = "family@group",
            DefaultColumn = PersonColumn.Rowan
        });

        Assert.Equal(PersonColumn.Rowan, mapper.Resolve(Event("family@group", "Family dinner")));
    }

    [Fact]
    public void Assign_SetsColumnOnEveryEvent()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            CalendarColumns = { ["w@group"] = PersonColumn.Jordan }
        });
        var events = new[] { Event("w@group"), Event("unmapped@group") };

        var assigned = mapper.Assign(events);

        Assert.Equal(PersonColumn.Jordan, assigned[0].Column);
        Assert.Equal(PersonColumn.Rowan, assigned[1].Column);
    }
}
