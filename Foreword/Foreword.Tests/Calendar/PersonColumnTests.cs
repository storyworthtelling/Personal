using System.Text.Json;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Calendar;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// A column is a name the owner chose, not a member of an enum.
///
/// <para>It used to be an enum of four people and Watch, which put the household's names in the
/// source of a public repository and made a second household impossible. What did not change is the
/// wire: the enum was always stored as the <b>word</b>, never the number, so a column read from a
/// document written by the old app is the same column it always was.</para>
/// </summary>
public class PersonColumnTests
{
    [Fact]
    public void TwoSpellingsOfOneNameAreOneColumn()
    {
        Assert.Equal(new PersonColumn("Alex"), new PersonColumn("alex"));
        Assert.Equal(new PersonColumn("Alex").GetHashCode(), new PersonColumn(" ALEX ").GetHashCode());
    }

    [Fact]
    public void ItPrintsAsTheNameItWasGiven()
    {
        Assert.Equal("Alex", new PersonColumn("Alex").ToString());
    }

    [Fact]
    public void WatchIsTheOneColumnTheCodeKnows()
    {
        Assert.Equal("Watch", PersonColumn.Watch.ToString());
        Assert.Equal(PersonColumn.Watch, new PersonColumn("watch"));
    }

    [Fact]
    public void ItIsStoredAsTheWord()
    {
        var json = CalendarMappingCodec.Write(new Dictionary<string, PersonColumn> { ["cal"] = Household.Alex });

        Assert.Equal("""{"cal":"Alex"}""", json);
    }

    /// <summary>
    /// The case the migration rests on: a document the old app wrote spells every column as a word,
    /// and that word reads back as the same column, with no table of names in the code.
    /// </summary>
    [Fact]
    public void AWordTheOldAppWroteReadsBackAsThatColumn()
    {
        var read = CalendarMappingCodec.Read<Dictionary<string, PersonColumn>>("""{"cal":"Riley"}""");

        Assert.Equal(Household.Riley, read!["cal"]);
    }

    [Fact]
    public void AColumnCanKeyADictionary()
    {
        var written = CalendarMappingCodec.Write(
            new Dictionary<PersonColumn, List<string>> { [Household.Sam] = ["sam@example.com"] });

        var read = CalendarMappingCodec.Read<Dictionary<PersonColumn, List<string>>>(written);

        Assert.Equal("""{"Sam":["sam@example.com"]}""", written);
        Assert.Equal(["sam@example.com"], read![new PersonColumn("sam")]);
    }

    [Fact]
    public void ANumberIsNotAColumn()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<PersonColumn>("0", CalendarMappingCodec.Options));
    }
}
