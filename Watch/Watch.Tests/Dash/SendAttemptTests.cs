using Watch.Domain.Dash;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// A reply from the wrist (W-5.1.2): dictated or typed, the same on the wire; named by the watch, so Post can answer it
/// and never send it twice (B2 keeps each send in its book). A send with no answer is asked again under the same name,
/// so a text that did go is not sent again; a send Post answered is a new send if the owner tries again.
/// </summary>
public class SendAttemptTests
{
    private static readonly Guid First = new("0123456789abcdef0123456789abcdef");
    private static readonly Guid Second = new("fedcba9876543210fedcba9876543210");

    [Fact]
    public void AReplyGoesToItsCorrespondence()
    {
        var attempt = SendAttempt.Reply(LeeKey, "  No rush, I’ll save you a spot by the bench. ", First);

        var message = attempt.Message;

        Assert.Equal((attempt.Id, LeeKey, "No rush, I’ll save you a spot by the bench."), (message.Id, message.Key, message.Text));
        Assert.Empty(message.To);
    }

    [Fact]
    public void TheNameIsTheWatchsOwn()
    {
        var attempt = SendAttempt.Reply(LeeKey, "On my way.", First);

        Assert.True(DashIds.IsAct(attempt.Id), attempt.Id);
        Assert.Equal("send-0123456789abcdef0123456789abcdef", attempt.Id);
    }

    [Fact]
    public void TwoSendsAreTwoNames() =>
        Assert.NotEqual(SendAttempt.Reply(LeeKey, "On my way.", First).Id, SendAttempt.Reply(LeeKey, "On my way.", Second).Id);

    [Fact]
    public void NoAnswerKeepsTheName()
    {
        var attempt = SendAttempt.Reply(LeeKey, "On my way.", First);

        Assert.Equal(attempt, attempt.Again(answered: null, Second));
    }

    [Theory]
    [InlineData(DashSendOutcome.NoRoute)]
    [InlineData(DashSendOutcome.Nowhere)]
    [InlineData(DashSendOutcome.Failed)]
    public void AnAnsweredSendIsANewOne(DashSendOutcome outcome)
    {
        var attempt = SendAttempt.Reply(LeeKey, "On my way.", First);

        var again = attempt.Again(outcome, Second);

        Assert.NotEqual(attempt.Id, again.Id);
        Assert.Equal((attempt.Key, attempt.Text), (again.Key, again.Text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingToSayIsNoSend(string words) => Assert.Equal(SendCheck.Empty, SendAttempt.Check(words));

    [Fact]
    public void TooLongForOneTextIsRefused()
    {
        Assert.Equal(SendCheck.TooLong, SendAttempt.Check(new string('a', DashLimits.SendText + 1)));
        Assert.Equal(SendCheck.Fits, SendAttempt.Check(new string('a', DashLimits.SendText)));
        Assert.Equal(SendCheck.Fits, SendAttempt.Check("On my way."));
    }

    [Fact]
    public void ARefusedTextIsNeverMade() =>
        Assert.Throws<ArgumentException>(() => SendAttempt.Reply(LeeKey, " ", First));

    [Fact]
    public void ItTravelsOnTheSendPath()
    {
        var message = SendAttempt.Reply(LeeKey, "On my way.", First).Message;

        var read = DashCodec.Decode(message.Path, DashCodec.Encode(message));

        Assert.True(read.Accepted);
        Assert.Equal(DashCodec.Encode(message), DashCodec.Encode(read.Message!));
    }
}
