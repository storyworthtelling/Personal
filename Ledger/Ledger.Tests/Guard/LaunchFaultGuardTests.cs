using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// A fault at launch must reach the owner: recorded on every route a crash can take, shown in full on
/// the next launch, and shown on the page instead of ending the app when the accounts page fails. These
/// are source-level guards because the paths run only on a device — each one reads the wiring by text,
/// so a later edit that drops the record or the catch fails here rather than on the owner's phone.
/// </summary>
public class LaunchFaultGuardTests
{
    private static string Source(string ledgerRelativePath)
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), ledgerRelativePath);
        Assert.True(File.Exists(path), $"Expected file not found: {ledgerRelativePath}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void EveryManagedAndTaskFaultRoutesThroughTheRecorder()
    {
        var application = Source("Ledger.Android/Platforms/Android/MainApplication.cs");

        Assert.Contains("AppDomain.CurrentDomain.UnhandledException", application, StringComparison.Ordinal);
        Assert.Contains("TaskScheduler.UnobservedTaskException", application, StringComparison.Ordinal);
        Assert.Contains("DefaultUncaughtExceptionHandler", application, StringComparison.Ordinal);
        Assert.Contains("LaunchFaults.Record(\"Unhandled exception\"", application, StringComparison.Ordinal);
        Assert.Contains("LaunchFaults.Record(\"Unobserved task exception\"", application, StringComparison.Ordinal);
        Assert.Contains("LaunchFaults.Record($\"Uncaught exception on thread", application, StringComparison.Ordinal);
    }

    [Fact]
    public void AFaultFromAnEarlierLaunchIsShownBeforeTheFirstPage()
    {
        var app = Source("Ledger.Android/App.xaml.cs");

        var create = NoDoubleLinkTests.ExtractMethod(app, "protected override Window CreateWindow(");
        Assert.Contains("LaunchFaults.PendingReport()", create, StringComparison.Ordinal);
        Assert.Contains("new Pages.LaunchFaultPage(pending, FirstPage)", create, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildingTheFirstPageIsGuardedAndItsFaultIsShown()
    {
        var app = Source("Ledger.Android/App.xaml.cs");

        var firstPage = NoDoubleLinkTests.ExtractMethod(app, "private Page FirstPage()");
        var guarded = firstPage.IndexOf("GetRequiredService<Pages.MainPage>()", StringComparison.Ordinal);
        var caught = firstPage.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(guarded >= 0 && caught > guarded, "The first page is built outside a catch.");
        Assert.Contains("LaunchFaults.Record(\"Building the first page\", ex)", firstPage, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAccountsPageShowsItsOwnFaultInsteadOfEndingTheApp()
    {
        var main = Source("Ledger.Android/Pages/MainPage.xaml.cs");

        var appearing = NoDoubleLinkTests.ExtractMethod(main, "protected override async void OnAppearing()");
        Assert.Contains("try", appearing, StringComparison.Ordinal);
        Assert.Contains("await ReloadAsync();", appearing, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", appearing, StringComparison.Ordinal);
        Assert.Contains("ShowLaunchFault(ex);", appearing, StringComparison.Ordinal);

        var show = NoDoubleLinkTests.ExtractMethod(main, "private void ShowLaunchFault(Exception ex)");
        Assert.Contains("LaunchFaults.Record(\"Accounts page, OnAppearing\", ex)", show, StringComparison.Ordinal);
        Assert.Contains("LaunchFaultPage.ReportView(report)", show, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFaultPageClearsTheReportOnlyWhenTheOwnerContinues()
    {
        var page = Source("Ledger.Android/Pages/LaunchFaultPage.cs");

        var clicked = page.IndexOf("continueButton.Clicked", StringComparison.Ordinal);
        var clear = page.IndexOf("LaunchFaults.ClearPending();", StringComparison.Ordinal);
        Assert.True(clicked >= 0 && clear > clicked, "The report is cleared outside the Continue action.");
    }
}
