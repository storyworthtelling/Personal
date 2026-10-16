namespace Pulp.Tests.Calendar;

/// <summary>
/// The head cannot be loaded here, so the follow-up's wiring (D302-D304) is checked by reading it: film
/// dates, plans, and the other adult's address each have to reach the one read the page and the nightly
/// writer share.
/// </summary>
public class CalendarFollowUpHeadGuardTests
{
    private static string Head(string relative)
    {
        var path = Path.Combine(PulpSource.Root(), relative);
        Assert.True(File.Exists(path), relative + " does not exist, so nothing it should contain can be checked.");
        return File.ReadAllText(path);
    }

    [Fact]
    public void TheOneReadCarriesFilmDatesAndPlansAndAFailingFilmSourceNeverBlanksTheCalendar()
    {
        var reads = Head("Pulp.Android/App/CalendarReads.cs");

        Assert.Contains("FilmDates:", reads, StringComparison.Ordinal);
        Assert.Contains("Plans: household.CalendarPlans", reads, StringComparison.Ordinal);
        Assert.Contains("FilmReleaseSource", reads, StringComparison.Ordinal);
        Assert.Contains("catch (Exception", reads, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFilmSourceIsTheCountedChangesFeedBehindACacheAndNeverTmdb()
    {
        var services = Head("Pulp.Android/App/PulpServices.cs");

        Assert.Contains("new CachedFilmReleaseSource(", services, StringComparison.Ordinal);
        Assert.Contains("new ChangesFilmReleaseSource(", services, StringComparison.Ordinal);
        Assert.DoesNotContain("tmdb", services, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheFeedSharesTheCalendarWithTheRecordedAdultAddresses()
    {
        var feed = Head("Pulp.Android/App/PulpCalendarFeed.cs");

        Assert.Contains("AdultGoogleAccounts", feed, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAPlanChecksBeforeItWritesAndRemovingOneIsUndoFirstOnTheDogEar()
    {
        var area = Head("Pulp.Android/Shell/CalendarArea.cs");

        Assert.Contains("Add a plan", area, StringComparison.Ordinal);
        Assert.Contains("CalendarPlans.Check(", area, StringComparison.Ordinal);
        Assert.Contains("HouseholdMaps.Plan", area, StringComparison.Ordinal);
        Assert.Contains("DatePickerDialog", area, StringComparison.Ordinal);
        Assert.Contains("DogEar.Show(", area, StringComparison.Ordinal);
        Assert.Contains("RemoveAsync(HouseholdMaps.Plan", area, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildBottomBar", area, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHouseholdPageChecksTheAddressAndDropsItWithThePerson()
    {
        var page = Head("Pulp.Android/Settings/HouseholdActivity.cs");

        Assert.Contains("GoogleAddress.Check(", page, StringComparison.Ordinal);
        Assert.Contains("HouseholdMaps.AdultAccount", page, StringComparison.Ordinal);
        Assert.Contains("RemoveAsync(HouseholdMaps.AdultAccount", page, StringComparison.Ordinal);
    }
}
