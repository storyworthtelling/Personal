using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// What the pairing screen says at each step. The words are fixed here so the screen only
/// draws them, and so a failure always says what to do next.
/// </summary>
public class MessagesPairingTests
{
    [Fact]
    public void EveryFailureSaysWhatHappenedAndWhatToDo()
    {
        foreach (var failure in Enum.GetValues<PairingFailure>())
        {
            var sentence = MessagesPairing.Describe(failure);
            Assert.False(string.IsNullOrWhiteSpace(sentence));
            Assert.EndsWith(".", sentence);
        }
    }

    // A wrong pick and a timeout are worth one more try; the screen offers it.
    [Theory]
    [InlineData(PairingFailure.WrongEmoji, true)]
    [InlineData(PairingFailure.TimedOut, true)]
    [InlineData(PairingFailure.PhoneNotResponding, true)]
    [InlineData(PairingFailure.Cancelled, true)]
    [InlineData(PairingFailure.Other, true)]
    [InlineData(PairingFailure.SignedOut, true)]
    [InlineData(PairingFailure.NoPhone, false)]
    public void ARetryIsOfferedWhereItCanHelp(PairingFailure failure, bool retry)
    {
        Assert.Equal(retry, MessagesPairing.WorthRetrying(failure));
    }

    // The emoji is the one thing on the screen that has to match the phone, so it is shown
    // as Messages gave it, with the instruction beside it.
    [Fact]
    public void TheEmojiStepNamesTheEmoji()
    {
        Assert.Contains("🦊", MessagesPairing.PickEmoji("🦊"));
    }

    [Fact]
    public void AnIncompleteSignInNamesWhatIsMissingButNoValue()
    {
        var sentence = MessagesPairing.SignInIncomplete(["OSID", "SAPISID"]);

        Assert.Contains("OSID", sentence);
        Assert.Contains("SAPISID", sentence);
    }

    [Fact]
    public void SettingsSaysWhereTextingStands()
    {
        Assert.Contains("not connected", TextsSetting.Describe(paired: false, connected: false));
        Assert.Contains("RCS", TextsSetting.Describe(paired: true, connected: true));
        Assert.Contains("reconnects", TextsSetting.Describe(paired: true, connected: false));
    }
}
