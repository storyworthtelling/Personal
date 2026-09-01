using Storyworthtelling.Voice.Health;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Health;

public class CredentialFieldStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NotSet_ShowsTheEntry()
    {
        var state = CredentialFieldState.Decide(isSet: false, CredentialHealth.Unknown);

        Assert.True(state.ShowEntry);
        Assert.False(state.IsWarning);
    }

    [Fact]
    public void SetAndOk_HidesTheEntryWithAWorkingStatus()
    {
        var state = CredentialFieldState.Decide(isSet: true, CredentialHealth.Ok(Now));

        Assert.False(state.ShowEntry);
        Assert.False(state.IsWarning);
        Assert.Contains("✓", state.StatusText);
    }

    [Fact]
    public void SetButFailing_RevealsTheEntryAsAWarning()
    {
        var state = CredentialFieldState.Decide(isSet: true, CredentialHealth.Failing("401 Unauthorized", Now));

        Assert.True(state.ShowEntry);
        Assert.True(state.IsWarning);
        Assert.Contains("401 Unauthorized", state.StatusText);
    }

    [Fact]
    public void SetButNotYetChecked_HidesTheEntryAsSaved()
    {
        // A key entered but never exercised is Unknown — don't nag with an open field, but don't
        // claim it's working either.
        var state = CredentialFieldState.Decide(isSet: true, CredentialHealth.Unknown);

        Assert.False(state.ShowEntry);
        Assert.False(state.IsWarning);
    }

    [Fact]
    public void FailingAlwaysWins_EvenWhenNotSet()
    {
        // A failure with no stored value still needs the entry open so it can be fixed.
        var state = CredentialFieldState.Decide(isSet: false, CredentialHealth.Failing("bad", Now));

        Assert.True(state.ShowEntry);
        Assert.True(state.IsWarning);
    }
}
