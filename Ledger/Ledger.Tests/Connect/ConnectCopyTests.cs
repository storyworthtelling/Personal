using Index.Domain.Connect;
using Xunit;

namespace Index.Tests.Connect;

/// <summary>
/// Mockup 5's words, verbatim. The copy lives in one Domain class so a test can hold it against the
/// drawing — a loose "contains" check is how copy drifts a word at a time while every test stays green.
/// </summary>
public class ConnectCopyTests
{
    [Fact]
    public void AdminModeNeeded_IsFrameOneVerbatim()
    {
        Assert.Equal("Admin mode needed", ConnectCopy.NeededHeading);
        Assert.Equal(
            "Your bank’s sign-in page opens in Chrome, and this phone only runs Chrome while Admin mode is open.",
            ConnectCopy.NeededWhy);
        Assert.Equal("Open Admin mode from Flyleaf’s settings, then come back here.", ConnectCopy.NeededWhere);
        Assert.Equal(
            "Balances don’t need it. Once a bank is connected they refresh every day without a browser.",
            ConnectCopy.NeededNotRequired);
        Assert.Equal("Connect on another phone instead", ConnectCopy.Fallback);
    }

    [Fact]
    public void AdminModeOpen_IsFrameTwoVerbatim()
    {
        Assert.Equal("Admin mode is open", ConnectCopy.OpenHeading);
        Assert.Equal(
            new[]
            {
                "Chrome opens Plaid’s page.",
                "Choose your bank and sign in there.",
                "Come back to Index. It notices the new bank on its own.",
            },
            ConnectCopy.OpenSteps);
        Assert.Equal("Open Plaid in Chrome", ConnectCopy.OpenPlaidButton);
    }

    [Fact]
    public void FinishingUp_IsFrameThreeVerbatim_AndNamesTheBankWhenKnown()
    {
        Assert.Equal("Finishing up", ConnectCopy.FinishingHeading);
        Assert.Equal("Waiting for Plaid to hand over Redwood Bank…", ConnectCopy.Waiting("Redwood Bank"));
        Assert.Equal(
            "This usually takes a few seconds. If Chrome closed before its last page, open it again.",
            ConnectCopy.FinishingBody);
        Assert.Equal("Open Chrome again", ConnectCopy.OpenChromeAgain);
        Assert.Equal("Check again", ConnectCopy.CheckAgainButton);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FinishingUp_NamesNoOneWhenTheBankIsNotKnownYet(string? name)
    {
        // A fresh link learns the institution only when Plaid hands it over, so the sentence cannot
        // invent a name — and must not print an empty slot either.
        Assert.Equal("Waiting for Plaid to hand over your bank…", ConnectCopy.Waiting(name));
    }

    [Fact]
    public void ChromeMissing_HasItsOwnPlainSentence_AndDoesNotSendTheOwnerToAdminMode()
    {
        Assert.DoesNotContain("Admin mode", ConnectCopy.ChromeMissingWhy);
        Assert.False(string.IsNullOrWhiteSpace(ConnectCopy.ChromeMissingHeading));
        Assert.NotEqual(ConnectCopy.NeededHeading, ConnectCopy.ChromeMissingHeading);
    }

    [Fact]
    public void NotConfigured_IsAPlainSentence_NotARawError()
    {
        Assert.Equal(
            "Enter your Plaid client ID and secret in Settings before connecting.",
            ConnectCopy.NotConfigured);
    }
}
