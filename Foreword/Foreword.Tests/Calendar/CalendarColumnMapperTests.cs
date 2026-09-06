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

        Assert.Equal(PersonColumn.Ashley, mapper.Resolve(Event("ashley-work@group")));
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
        var mapper = CreateMapper(new CalendarMappingOptions { DefaultColumn = PersonColumn.Matt });

        Assert.Equal(PersonColumn.Matt, mapper.Resolve(Event("church@group")));
    }

    [Fact]
    public void Resolve_FamilyCalendarRoutesGwenByKeyword()
    {
        var mapper = CreateMapper(new CalendarMappingOptions { FamilyCalendarId = "family@group" });

        Assert.Equal(PersonColumn.Gwen, mapper.Resolve(Event("family@group", "Gwen dentist appt")));
    }

    [Fact]
    public void Resolve_FamilyCalendarRoutesEvaByKeyword()
    {
        var mapper = CreateMapper(new CalendarMappingOptions { FamilyCalendarId = "family@group" });

        Assert.Equal(PersonColumn.Eva, mapper.Resolve(Event("family@group", "Eva soccer practice")));
    }

    [Fact]
    public void Resolve_FamilyCalendarWithoutKidKeywordUsesDefault()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            FamilyCalendarId = "family@group",
            DefaultColumn = PersonColumn.Matt
        });

        Assert.Equal(PersonColumn.Matt, mapper.Resolve(Event("family@group", "Family dinner")));
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

        Assert.Equal(PersonColumn.Ashley, assigned[0].Column);
        Assert.Equal(PersonColumn.Matt, assigned[1].Column);
    }
}
