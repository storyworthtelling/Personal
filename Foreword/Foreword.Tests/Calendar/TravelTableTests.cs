using Foreword.Domain.Calendar;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// The measured drives for one day, and the house they start from.
///
/// <para>It is a table rather than a number on each event because a drive has two ends and the
/// columns do not share them: the same field is a different drive from the office than it is from
/// the school. And it is looked up by the location <b>text</b>, which is what a calendar actually
/// carries — typed by whoever made the event, so "Veterans Park" and "veterans park " are the same
/// field and a table that thinks otherwise quietly measures nothing.</para>
/// </summary>
public class TravelTableTests
{
    [Fact]
    public void ALegThatWasMeasuredComesBack()
    {
        var table = TravelTable.Of("Home", [("Home", "The school", 12)]);

        Assert.Equal(12, table.Minutes("Home", "The school"));
    }

    [Fact]
    public void ALegNobodyMeasuredIsUnknownRatherThanZero()
    {
        var table = TravelTable.Of("Home", [("Home", "The school", 12)]);

        Assert.Null(table.Minutes("Home", "Veterans Park"));
    }

    /// <summary>
    /// A drive is not symmetrical. Rush hour runs one way in the morning and the other at six, and
    /// the table holds each direction it was given rather than assuming the return leg.
    /// </summary>
    [Fact]
    public void TheReturnLegIsNotAssumed()
    {
        var table = TravelTable.Of("Home", [("Home", "The office", 15)]);

        Assert.Null(table.Minutes("The office", "Home"));
    }

    /// <summary>
    /// Location text is typed by people, so the lookup forgives what people do to it. Getting this
    /// wrong is the kind of failure that shows up as nothing at all: every band silently missing,
    /// no error anywhere, and a page that looks exactly like a day with no drives in it.
    /// </summary>
    [Fact]
    public void CaseAndSurroundingSpaceAreNotDifferentPlaces()
    {
        var table = TravelTable.Of("Home", [(" Home ", "Veterans PARK", 25)]);

        Assert.Equal(25, table.Minutes("home", " veterans park"));
    }

    [Fact]
    public void ADayWithNothingMeasuredAnswersNothing()
    {
        Assert.Null(TravelTable.NothingMeasured.Minutes("Home", "Veterans Park"));
    }

    /// <summary>
    /// The house is where every column's morning starts, so an empty table still has to name one —
    /// otherwise the first drive of the day would be measured from nowhere.
    /// </summary>
    [Fact]
    public void ThereIsAlwaysAHomeToStartFrom()
    {
        Assert.False(string.IsNullOrWhiteSpace(TravelTable.NothingMeasured.Home));
    }
}
