namespace Pulp.Tests.Calendar;

/// <summary>
/// The Android head cannot be loaded here, so the Calendar page's and the nightly writer's wiring is checked
/// by reading it. Each guard names the bug it keeps out and first asserts it found the file it reads.
/// </summary>
public class CalendarScreenHeadGuardTests
{
    private static string Head(string relative)
    {
        var path = Path.Combine(PulpSource.Root(), relative);
        Assert.True(File.Exists(path), relative + " does not exist, so nothing it should contain can be checked.");
        var text = File.ReadAllText(path);
        Assert.False(string.IsNullOrWhiteSpace(text));
        return text;
    }

    [Fact]
    public void TheDailyWorkIsScheduledUnderTheFrozenNameAndKept()
    {
        // Bug kept out: nothing scheduled the frozen worker, so the calendar was never written; or a REPLACE
        // policy / a new name that leaves two schedules (or none) on a phone that already has one.
        var text = Head("Pulp.Android/Calendar/CalendarSchedule.cs");

        Assert.Contains("PulpIdentity.DailyChangesWorkName", text, StringComparison.Ordinal);
        Assert.Contains("ExistingPeriodicWorkPolicy.Keep", text, StringComparison.Ordinal);
        Assert.Contains("typeof(CalendarSyncWorker)", text, StringComparison.Ordinal);
        Assert.Contains("CalendarScreenSchedule.DelayUntilNextRun", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ExistingPeriodicWorkPolicy.Replace", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCompositionBuildsTheCalendarOnlyFromTheClientsOwnScopesAndHooksTheWorker()
    {
        // Bug kept out: a hook left unset (the worker succeeds and writes nothing, silently), or a calendar
        // account built from any scope list but the client's own.
        var text = Head("Pulp.Android/App/PulpServices.cs");

        Assert.Contains("new VoiceGoogleAccount(GoogleCalendarClient.Scopes.ToArray())", text, StringComparison.Ordinal);
        Assert.Contains("CalendarSyncHooks.Calendar = new GoogleCalendarClient(", text, StringComparison.Ordinal);
        Assert.Contains("CalendarSyncHooks.Feed = ", text, StringComparison.Ordinal);
        Assert.Contains("CalendarSyncHooks.Warn = ", text, StringComparison.Ordinal);
        Assert.Contains("CalendarSchedule.Schedule(", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkerComposesTheServicesItNeedsBeforeItReadsTheHooks()
    {
        // Bug kept out: WorkManager starts the process without the shell, so the hooks were still null and
        // the worker reported success having done nothing.
        var text = Head("Pulp.Android/Calendar/CalendarSyncWorker.cs");

        var compose = text.IndexOf("PulpServices.For(", StringComparison.Ordinal);
        var read = text.IndexOf("CalendarSyncHooks.Feed;", StringComparison.Ordinal);
        Assert.True(compose >= 0, "The worker never asks for the composition.");
        Assert.True(read >= 0);
        Assert.True(compose < read, "The worker reads the hooks before composing them.");
    }

    [Fact]
    public void TheFeedWritesTheCalendarIdToTheHouseholdAndAsksNothingOfAnAdultWhoHoldsNothing()
    {
        // Bug kept out: the id kept only on one phone (a second calendar made on the next run), or a held-
        // leagues table built only for adults who hold something (ForHousehold throws for a missing adult).
        var feed = Head("Pulp.Android/App/PulpCalendarFeed.cs");
        var reads = Head("Pulp.Android/App/CalendarReads.cs");

        Assert.Contains("HouseholdMaps.PulpCalendar", feed, StringComparison.Ordinal);
        Assert.Contains("CalendarScreenSnapshot.Build(", feed, StringComparison.Ordinal);
        Assert.Contains("CalendarScreenHolds.ByAdult(", reads, StringComparison.Ordinal);
        Assert.Contains("CalendarScreenSources.Build(", reads, StringComparison.Ordinal);

        // The behaviour is CalendarFollowsTests; this keeps the read going through it. A follow by a child,
        // a removed viewer or a null roster made ForHousehold throw, or put a child's name on the calendar.
        Assert.Contains("CalendarFollows.OfAdults(household.SportsFollows, roster)", reads, StringComparison.Ordinal);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(reads, @"household\.SportsFollows").Count);
    }

    [Fact]
    public void FollowingATeamIsRefusedForAChild()
    {
        var text = Head("Pulp.Android/App/PulpServices.cs");
        var start = text.IndexOf("public async Task FollowTeamAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0, "FollowTeamAsync is missing or no longer async, so it cannot read the roster.");
        var body = text[start..Math.Min(text.Length, start + 900)];

        Assert.Contains("CalendarFollows.RefusesFollow(", body, StringComparison.Ordinal);
        Assert.Contains("HouseholdMaps.SportsFollow", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkerCarriesTheFrozenNameAndAnswersEveryFailureNotOnlyTheCalendarsOwn()
    {
        // Bugs kept out: a renamed worker class (WorkManager cannot instantiate a persisted row), and an
        // uncaught non-calendar exception (a sign-in or programming error) crashing the worker unreported.
        var text = Head("Pulp.Android/Calendar/CalendarSyncWorker.cs");

        Assert.Contains("[global::Android.Runtime.Register(PulpIdentity.DailyChangesWorkerName)]", text, StringComparison.Ordinal);
        Assert.Contains("class CalendarSyncWorker : Worker", text, StringComparison.Ordinal);
        Assert.Contains("catch (Exception exception)", text, StringComparison.Ordinal);
        Assert.Contains("CalendarFailures.Advise(", text, StringComparison.Ordinal);
        Assert.Contains("Result.InvokeRetry()", text, StringComparison.Ordinal);
        Assert.Contains("Result.InvokeFailure()", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ARejectedTokenDisconnectsTheCalendarOnlyWhenConsentWasWithdrawn()
    {
        // Bug kept out: every 403 cleared the connected flag, so a rate-limit blip disconnected the calendar.
        var text = Head("Pulp.Android/App/PulpServices.cs");

        Assert.Contains("CalendarFailures.ConsentWithdrawn(exception)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CalendarFailure.Rejected }", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCalendarSignInNeverRepointsTheDriveAccount()
    {
        var text = Head("Pulp.Android/Calendar/CalendarConnection.cs");
        Assert.Contains("CalendarScreenConnection.JudgeAccount(", text, StringComparison.Ordinal);
        Assert.Contains("CalendarScreenConnection.DifferentAccount(", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheScopeCommentAdmitsTheSignInAlsoCarriesDrive()
    {
        var text = Head("Pulp.Infrastructure/Calendar/GoogleCalendarClient.cs");
        Assert.DoesNotContain("narrowest pair", text, StringComparison.Ordinal);
        Assert.Contains("also carries Drive", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCalendarPageStaysWithinTheRulesOfAPageThatOnlyReads()
    {
        // Bugs kept out: the raw feed reached from a screen (a standing or a highlight of a held league);
        // a bottom bar (D246); OpenOn, which finishes the shell the page lives in; a type-only cancellation
        // filter; a Caution role anywhere in Pulp.
        var text = Head("Pulp.Android/Shell/CalendarArea.cs");

        Assert.DoesNotContain("FollowedFeedsAsync", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildBottomBar", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MainActivity.OpenOn(", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Caution", text, StringComparison.Ordinal);
        Assert.Contains("CancellationVerdict.IsUserCancel(", text, StringComparison.Ordinal);
        Assert.Contains("TitleActivity.For(", text, StringComparison.Ordinal);
        Assert.Contains("AreaRequest.Word(PulpArea.Sports)", text, StringComparison.Ordinal);
        Assert.Contains("CalendarScreen.StatusLine", text, StringComparison.Ordinal);
        Assert.Contains("VoiceSeat.EmptyCalendar", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCalendarConnectionKeepsItsFlagAndNamesItsSentenceFromTheDomain()
    {
        // Bug kept out: the Settings and Vault pages inventing their own words for the calendar's state.
        var text = Head("Pulp.Android/Calendar/CalendarConnection.cs");

        Assert.Contains("CalendarScreenConnection.Sentence(", text, StringComparison.Ordinal);
        Assert.Contains("CalendarScreenConnection.OffersConnect(", text, StringComparison.Ordinal);
    }
}
