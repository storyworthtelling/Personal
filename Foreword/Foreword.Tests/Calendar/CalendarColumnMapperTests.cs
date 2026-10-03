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
        new(Options.Create(WithHousehold(options)),
            Options.Create(new Foreword.Infrastructure.Install.InstallOptions { AccountEmail = accountEmail }));

    // The household's own setup, as Foreword's Columns screen would have stored it: the four people,
    // the two with accounts, and each person's name as the word that routes to them. A test that
    // sets its own columns or words keeps them.
    private static CalendarMappingOptions WithHousehold(CalendarMappingOptions options)
    {
        if (options.Columns.Count == 0)
        {
            options.Columns = Household.Layout.People
                .Select(p => new ColumnChoice { Name = p.Name, HasAccount = Household.Layout.HasAccount(p) })
                .ToList();
        }

        foreach (var person in Household.Layout.People)
        {
            options.Keywords.TryAdd(person, [person.Name]);
        }

        return options;
    }

    private static CalendarEvent Event(string calendarId, string title = "Thing") =>
        new() { Title = title, SourceCalendarId = calendarId };

    [Fact]
    public void Resolve_UsesExplicitCalendarMapping()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            CalendarColumns = { ["sam-work@group"] = Household.Sam }
        });

        Assert.Equal([Household.Sam], mapper.Resolve(Event("sam-work@group")));
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
        var mapper = CreateMapper(new CalendarMappingOptions { DefaultColumn = Household.Alex });

        Assert.Equal([Household.Alex], mapper.Resolve(Event("church@group")));
    }

    [Fact]
    public void Resolve_FamilyCalendarRoutesRileyByKeyword()
    {
        var mapper = CreateMapper(new CalendarMappingOptions { FamilyCalendarId = "family@group" });

        Assert.Equal([Household.Riley], mapper.Resolve(Event("family@group", "Riley dentist appt")));
    }

    [Fact]
    public void Resolve_FamilyCalendarRoutesJoByKeyword()
    {
        var mapper = CreateMapper(new CalendarMappingOptions { FamilyCalendarId = "family@group" });

        Assert.Equal([Household.Jo], mapper.Resolve(Event("family@group", "Jo soccer practice")));
    }

    /// <summary>
    /// CHANGED BY D31, and this is the change stated plainly. An event on the shared calendar that
    /// names nobody used to fall to the default column — so "Family dinner" was Alex's, and only
    /// Alex's. Nobody named on a shared calendar now means EVERYBODY, which is what a calendar
    /// called Family means.
    /// </summary>
    [Fact]
    public void Resolve_SharedCalendarWithNobodyNamedMeansEverybody()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            FamilyCalendarId = "family@group",
            DefaultColumn = Household.Alex
        });

        Assert.Equal(Household.Layout.People, mapper.Resolve(Event("family@group", "Family dinner")));
    }

    /// <summary>Every name in the title, not the first — the bug D31 exists to fix.</summary>
    [Fact]
    public void Resolve_SharedCalendarSplitsOnEveryNameInTheTitle()
    {
        var mapper = CreateMapper(new CalendarMappingOptions { FamilyCalendarId = "family@group" });

        Assert.Equal(
            [Household.Riley, Household.Jo],
            mapper.Resolve(Event("family@group", "Riley and Jo to swimming")));
    }

    /// <summary>An invited address is a fact; it is read before the title (D31).</summary>
    [Fact]
    public void Resolve_SharedCalendarReadsTheGuestsBeforeTheTitle()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            FamilyCalendarId = "family@group",
            PersonAddresses = { [Household.Jo] = ["jo@example.com"] },
        });

        var invited = new CalendarEvent
        {
            Title = "Riley to swimming",
            SourceCalendarId = "family@group",
            Guests = ["jo@example.com"],
        };

        Assert.Equal([Household.Jo], mapper.Resolve(invited));
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
            [Household.Riley, Household.Jo],
            mapper.Resolve(Event("scouts@group", "Riley and Jo to the pack meeting")));
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

        Assert.Equal(Household.Layout.People, mapper.Resolve(Event("holidays@group", "Thanksgiving")));
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
            CalendarColumns = { ["alex@work"] = Household.Alex },
            FamilyCalendarId = "family@group",
            SplitCalendars = { "scouts@group" },
            EveryoneCalendars = { "holidays@group" },
            NotPrintedCalendars = { "spam@group" },
        });

        var known = mapper.KnownCalendars();

        Assert.Equal(
            new[] { "alex@work", "family@group", "holidays@group", "scouts@group", "spam@group" },
            known.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Assign_SetsColumnOnEveryEvent()
    {
        var mapper = CreateMapper(new CalendarMappingOptions
        {
            CalendarColumns = { ["w@group"] = Household.Sam }
        });
        var events = new[] { Event("w@group"), Event("unmapped@group") };

        var assigned = mapper.Assign(events);

        Assert.Equal([Household.Sam], assigned[0].Columns);
        Assert.Equal([Household.Alex], assigned[1].Columns);
    }
}
