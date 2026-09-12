using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;
using Microsoft.Extensions.Options;
using Xunit;

namespace Foreword.Tests.Calendar;

public class CalendarColumnMapperTests
{
    private static CalendarColumnMapper CreateMapper(
        CalendarMappingOptions options, string accountEmail = "") =>
        new(Options.Create(options),
            Options.Create(new Foreword.Infrastructure.Install.InstallOptions { AccountEmail = accountEmail }));

    private static CalendarEvent Event(string calendarId, string title = "Thing") =>
        new() { Title = title, SourceCalendarId = calendarId };

    [Fact]
    public void Resolve_UsesExplicitCalendarMapping()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            CalendarColumns = { ["jordan-work@group"] = PersonColumn.Jordan }
        });

        Assert.Equal([PersonColumn.Jordan], mapper.Resolve(Event("jordan-work@group")));
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
        var mapper = CreateMapper(new CalendarMappingOptions { DefaultColumn = PersonColumn.Rowan });

        Assert.Equal([PersonColumn.Rowan], mapper.Resolve(Event("church@group")));
    }

    [Fact]
    public void Resolve_FamilyCalendarRoutesGwenByKeyword()
    {
        var mapper = CreateMapper(new CalendarMappingOptions { FamilyCalendarId = "family@group" });

        Assert.Equal([PersonColumn.Wren], mapper.Resolve(Event("family@group", "Wren dentist appt")));
    }

    [Fact]
    public void Resolve_FamilyCalendarRoutesEvaByKeyword()
    {
        var mapper = CreateMapper(new CalendarMappingOptions { FamilyCalendarId = "family@group" });

        Assert.Equal([PersonColumn.Linden], mapper.Resolve(Event("family@group", "Linden soccer practice")));
    }

    /// <summary>
    /// CHANGED BY D31, and this is the change stated plainly. An event on the shared calendar that
    /// names nobody used to fall to the default column — so "Family dinner" was Rowan's, and only
    /// Rowan's. Nobody named on a shared calendar now means EVERYBODY, which is what a calendar
    /// called Family means.
    /// </summary>
    [Fact]
    public void Resolve_SharedCalendarWithNobodyNamedMeansEverybody()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            FamilyCalendarId = "family@group",
            DefaultColumn = PersonColumn.Rowan
        });

        Assert.Equal(ColumnSet.People, mapper.Resolve(Event("family@group", "Family dinner")));
    }

    /// <summary>Every name in the title, not the first — the bug D31 exists to fix.</summary>
    [Fact]
    public void Resolve_SharedCalendarSplitsOnEveryNameInTheTitle()
    {
        var mapper = CreateMapper(new CalendarMappingOptions { FamilyCalendarId = "family@group" });

        Assert.Equal(
            [PersonColumn.Wren, PersonColumn.Linden],
            mapper.Resolve(Event("family@group", "Wren and Linden to swimming")));
    }

    /// <summary>An invited address is a fact; it is read before the title (D31).</summary>
    [Fact]
    public void Resolve_SharedCalendarReadsTheGuestsBeforeTheTitle()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            FamilyCalendarId = "family@group",
            PersonAddresses = { [PersonColumn.Linden] = ["linden@example.com"] },
        });

        var invited = new CalendarEvent
        {
            Title = "Wren to swimming",
            SourceCalendarId = "family@group",
            Guests = ["linden@example.com"],
        };

        Assert.Equal([PersonColumn.Linden], mapper.Resolve(invited));
    }

    /// <summary>D33: split by name is available to any calendar, not only the family one.</summary>
    [Fact]
    public void Resolve_AnyCalendarCanBeSplitByName()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            SplitCalendars = { "scouts@group" },
        });

        Assert.Equal(
            [PersonColumn.Wren, PersonColumn.Linden],
            mapper.Resolve(Event("scouts@group", "Wren and Linden to the pack meeting")));
    }

    /// <summary>
    /// An "everyone" calendar needs nobody named. A holiday calendar is the case: the day is the
    /// whole family's without anyone being invited to it.
    /// </summary>
    [Fact]
    public void Resolve_AnEveryoneCalendarGoesToEveryColumn()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            EveryoneCalendars = { "holidays@group" },
        });

        Assert.Equal(ColumnSet.People, mapper.Resolve(Event("holidays@group", "Thanksgiving")));
    }

    /// <summary>
    /// A calendar the owner has answered is KNOWN, whatever the answer was — including "not
    /// printed". Holding a calendar they have already told the app to ignore would be nagging.
    /// </summary>
    [Fact]
    public void KnownCalendars_IncludesEveryCalendarTheOwnerHasAnswered()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            CalendarColumns = { ["rowan@work"] = PersonColumn.Rowan },
            FamilyCalendarId = "family@group",
            SplitCalendars = { "scouts@group" },
            EveryoneCalendars = { "holidays@group" },
            NotPrintedCalendars = { "spam@group" },
        });

        var known = mapper.KnownCalendars();

        Assert.Equal(
            new[] { "family@group", "holidays@group", "rowan@work", "scouts@group", "spam@group" },
            known.Order(StringComparer.Ordinal));
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

        Assert.Equal([PersonColumn.Jordan], assigned[0].Columns);
        Assert.Equal([PersonColumn.Rowan], assigned[1].Columns);
    }
}
