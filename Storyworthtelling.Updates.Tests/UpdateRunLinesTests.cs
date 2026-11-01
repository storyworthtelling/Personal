namespace Storyworthtelling.Updates.Tests;

// Flyleaf's update runs for the log loop (plan §4.5): one line per app per run, not just the last summary
// sentence, so a signature mismatch that keeps an app back is visible app by app.
public class UpdateRunLinesTests
{
    [Fact]
    public void ARunNamesEveryAppAndItsOutcome()
    {
        var run = UpdateRunResult.Ran([
            new AppInstallResult("com.storyworthtelling.focusphone.communication", 200123, InstallOutcome.Installed),
            new AppInstallResult("com.storyworthtelling.pulp", 900045, InstallOutcome.Failed, "INSTALL_FAILED_UPDATE_INCOMPATIBLE"),
            new AppInstallResult("com.storyworthtelling.ledger", 100077, InstallOutcome.AlreadyCurrent),
        ]);

        Assert.Equal(
        [
            "UPDATE com.storyworthtelling.focusphone.communication 200123: Installed",
            "UPDATE com.storyworthtelling.pulp 900045: Failed — INSTALL_FAILED_UPDATE_INCOMPATIBLE",
            "UPDATE com.storyworthtelling.ledger 100077: AlreadyCurrent",
        ], run.Lines());
    }

    [Fact]
    public void ARunThatNeverReachedTheAppsSaysWhy()
    {
        Assert.Equal(["UPDATE run NoToken"], UpdateRunResult.NoToken().Lines());
        Assert.Equal(["UPDATE run FetchFailed — 401 Unauthorized"], UpdateRunResult.FetchFailed("401 Unauthorized").Lines());
        Assert.Equal(["UPDATE run Ran: nothing new"], UpdateRunResult.Ran([]).Lines());
    }
}
