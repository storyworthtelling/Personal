using Codex.Domain.Updates;
using Xunit;

namespace Codex.Tests.Updates;

public class UpdateRunResultTests
{
    [Fact]
    public void NoToken_SaysSo()
    {
        Assert.Equal("No update token is set, so updates can't be checked.",
            UpdateRunResult.NoToken().Summarize());
    }

    [Fact]
    public void NotDeviceOwner_SaysSo()
    {
        Assert.Equal("Not the device owner, so updates can't install.",
            UpdateRunResult.NotDeviceOwner().Summarize());
    }

    [Fact]
    public void FetchFailed_CarriesTheReason()
    {
        Assert.Equal("Couldn't reach GitHub (401 Unauthorized).",
            UpdateRunResult.FetchFailed("401 Unauthorized").Summarize());
    }

    [Fact]
    public void Ran_WithNothingToDo_SaysUpToDate()
    {
        var result = UpdateRunResult.Ran(
        [
            new AppInstallResult("com.storyworthtelling.focusphone.launcher", 128, InstallOutcome.AlreadyCurrent),
        ]);

        Assert.Equal("Everything is up to date.", result.Summarize());
        Assert.False(result.AnyInstalled);
        Assert.False(result.AnyFailed);
    }

    [Fact]
    public void Ran_WithInstalls_CountsThem()
    {
        var result = UpdateRunResult.Ran(
        [
            new AppInstallResult("com.storyworthtelling.focusphone.launcher", 128, InstallOutcome.Installed),
            new AppInstallResult("com.storyworthtelling.focusphone.audio", 128, InstallOutcome.Installed),
            new AppInstallResult("com.storyworthtelling.ledger", 40, InstallOutcome.AlreadyCurrent),
        ]);

        Assert.Equal("Installed 2 updates.", result.Summarize());
        Assert.True(result.AnyInstalled);
    }

    [Fact]
    public void Ran_WithSingleInstall_UsesSingular()
    {
        var result = UpdateRunResult.Ran(
        [
            new AppInstallResult("com.storyworthtelling.focusphone.launcher", 128, InstallOutcome.Installed),
        ]);

        Assert.Equal("Installed 1 update.", result.Summarize());
    }

    [Fact]
    public void Ran_WithFailure_ReportsItAndTheReason()
    {
        var result = UpdateRunResult.Ran(
        [
            new AppInstallResult("com.storyworthtelling.focusphone.launcher", 128, InstallOutcome.Failed,
                "Install failed (status 4): signatures do not match"),
        ]);

        Assert.Equal("1 couldn't install (Install failed (status 4): signatures do not match).",
            result.Summarize());
        Assert.True(result.AnyFailed);
    }

    [Fact]
    public void Ran_WithInstallAndFailure_ReportsBoth()
    {
        var result = UpdateRunResult.Ran(
        [
            new AppInstallResult("com.storyworthtelling.focusphone.audio", 128, InstallOutcome.Installed),
            new AppInstallResult("com.storyworthtelling.focusphone.launcher", 128, InstallOutcome.Failed,
                "signatures do not match"),
        ]);

        Assert.Equal("Installed 1 update. 1 couldn't install (signatures do not match).",
            result.Summarize());
    }

    [Theory]
    [InlineData(InstallOutcome.Installed, true)]
    [InlineData(InstallOutcome.AlreadyCurrent, true)]
    [InlineData(InstallOutcome.Unreadable, true)]
    [InlineData(InstallOutcome.Failed, false)]
    public void ShouldMarkProcessed_RemembersEverythingButFailures(InstallOutcome outcome, bool expected)
    {
        Assert.Equal(expected, InstallOutcomes.ShouldMarkProcessed(outcome));
    }
}
