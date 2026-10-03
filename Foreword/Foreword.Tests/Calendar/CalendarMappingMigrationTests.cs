using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// The document the old app wrote becomes this one, on the phone, with nothing re-entered.
///
/// <para>The old shape named its people in two places: as the words in the column values (which
/// read straight across, see <see cref="PersonColumnTests"/>) and as property names — one
/// <c>&lt;Name&gt;Keywords</c> list per person. Those are read generically, by their shape, so the
/// migration never needs to know whose names they are. The layout's <b>order</b> is recovered the
/// same way: a column whose person can be invited — an address entered for them, or their own
/// primary calendar mapped to them — is somebody with an account, and those came first.</para>
/// </summary>
public class CalendarMappingMigrationTests
{
    // The old app's shape, with invented people. Property order is the old class's declaration
    // order, which put the two without accounts first — the migration must not copy that order.
    private const string OldDocument = """
        {
          "CalendarColumns": {
            "alex@example.com": "Alex",
            "sam@example.com": "Sam",
            "abc123@group.calendar.google.com": "Riley",
            "def456@group.calendar.google.com": "Jo",
            "fixtures@group.v.calendar.google.com": "Watch"
          },
          "FamilyCalendarId": "family@group.calendar.google.com",
          "DefaultColumn": null,
          "RileyKeywords": ["Riley", "Ri"],
          "JoKeywords": ["Jo"],
          "AlexKeywords": ["Alex"],
          "SamKeywords": ["Sam", "Sammy"],
          "PersonAddresses": {}
        }
        """;

    private static CalendarMappingOptions Migrated(string json = OldDocument) =>
        CalendarMappingCodec.ReadMapping(json)!;

    [Fact]
    public void PeopleWithAnAccountComeFirstThenTheRestInTheOrderTheyWereWritten()
    {
        var layout = Migrated().Layout();

        Assert.Equal([Household.Alex, Household.Sam, Household.Riley, Household.Jo], layout.People);
        Assert.Equal([Household.Alex, Household.Sam], layout.WithAccounts);
    }

    [Fact]
    public void EachPersonsWordsMoveAcrossWithThem()
    {
        var mapping = Migrated();

        Assert.Equal(["Riley", "Ri"], mapping.Keywords[Household.Riley]);
        Assert.Equal(["Sam", "Sammy"], mapping.Keywords[Household.Sam]);
    }

    [Fact]
    public void TheCalendarRulesAreUntouched()
    {
        var mapping = Migrated();

        Assert.Equal(Household.Jo, mapping.CalendarColumns["def456@group.calendar.google.com"]);
        Assert.Equal(PersonColumn.Watch, mapping.CalendarColumns["fixtures@group.v.calendar.google.com"]);
        Assert.Equal("family@group.calendar.google.com", mapping.FamilyCalendarId);
    }

    /// <summary>
    /// Written back, the document has the new shape and none of the old per-person properties —
    /// otherwise every save would carry the old lists forward and they would win again on the next
    /// read.
    /// </summary>
    [Fact]
    public void WrittenBackItHasNoPerPersonProperties()
    {
        var written = CalendarMappingCodec.Write(Migrated());

        Assert.DoesNotContain("RileyKeywords", written);
        Assert.Contains("\"Columns\"", written);
        Assert.Contains("\"Keywords\"", written);
    }

    [Fact]
    public void AnAddressEnteredForSomebodyMakesThemSomebodyWithAnAccount()
    {
        var layout = Migrated("""
            {
              "CalendarColumns": { "a@group.calendar.google.com": "Riley" },
              "RileyKeywords": ["Riley"],
              "SamKeywords": ["Sam"],
              "PersonAddresses": { "Sam": ["sam@example.com"] }
            }
            """).Layout();

        Assert.Equal([Household.Sam, Household.Riley], layout.People);
        Assert.Equal([Household.Sam], layout.WithAccounts);
    }

    /// <summary>A document already in the new shape is read as it is, order and all.</summary>
    [Fact]
    public void ANewDocumentKeepsItsOwnLayout()
    {
        var layout = Migrated("""
            {
              "Columns": [ { "Name": "Jo", "HasAccount": false }, { "Name": "Alex", "HasAccount": true } ],
              "CalendarColumns": { "alex@example.com": "Alex" }
            }
            """).Layout();

        Assert.Equal([Household.Jo, Household.Alex], layout.People);
        Assert.Equal([Household.Alex], layout.WithAccounts);
    }

    [Fact]
    public void AnEmptyDocumentHasNoColumnsYet()
    {
        Assert.Null(CalendarMappingCodec.ReadMapping(string.Empty));
        Assert.Empty(Migrated("{}").Columns);
    }

    [Fact]
    public void AGarbledDocumentReadsAsAbsent()
    {
        Assert.Null(CalendarMappingCodec.ReadMapping("{ not json"));
    }
}
