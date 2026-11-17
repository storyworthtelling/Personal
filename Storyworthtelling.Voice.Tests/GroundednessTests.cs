using Moq;
using Storyworthtelling.Voice;
using Xunit;

namespace Storyworthtelling.Voice.Tests;

/// <summary>
/// The facts check (audit W10.5): a reply that states a number, a name or a claim its request never gave is logged as
/// <c>VOICE UNGROUNDED &lt;seat&gt;</c>. The line is still shown; the log is how a drift is found, and it never carries
/// the reply or the facts.
/// </summary>
public class GroundednessTests
{
    [Fact]
    public void ANumberTheFactsNeverGaveIsUngrounded()
    {
        Assert.Contains("12", Groundedness.Ungrounded("Twelve down, 12 to go.", "Three tasks are on the list."));
        Assert.Contains("Twelve", Groundedness.Ungrounded("Twelve down.", "Three tasks are on the list."));
    }

    [Fact]
    public void ANumberTheFactsGaveIsGrounded()
    {
        Assert.Empty(Groundedness.Ungrounded("Three tasks, and the first at 8:30.", "Three tasks. The first begins at 8:30 am."));
        Assert.Empty(Groundedness.Ungrounded("Down 12 percent.", "Debt is down 12 percent."));
    }

    [Fact]
    public void ANameTheFactsNeverGaveIsUngrounded()
    {
        // A capitalised word inside a sentence is a name; the first word of a sentence is not.
        Assert.Contains("Margot", Groundedness.Ungrounded("Lunch with Margot, then the dentist.", "Two events today."));
        Assert.Empty(Groundedness.Ungrounded("Lunch first. Then the dentist.", "Two events today: lunch and the dentist."));
        Assert.Empty(Groundedness.Ungrounded("The Salt Line keeps moving.", "Title: The Salt Line."));
    }

    [Fact]
    public void AClaimTheFactsNeverMadeIsUngrounded()
    {
        Assert.Contains("finished", Groundedness.Ungrounded("The draft is finished.", "The review is still in progress."));
        Assert.Empty(Groundedness.Ungrounded("Finished and delivered.", "The revision report is finished and delivered."));
    }

    [Fact]
    public async Task TheMessengerLogsAnUngroundedReplyBySeatAndNothingElse()
    {
        var store = new Mock<IVoiceProfileStore>();
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(VoiceProfile.Empty with { Register = "dry", DocumentsAnalyzed = 1 });
        var client = new Mock<IVoiceClient>();
        client.Setup(c => c.ComposeAsync(It.IsAny<VoiceProfile>(), It.IsAny<MessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Twelve things, and Margot is waiting.");
        var lines = new List<string>();

        var reply = await new VoicedMessenger(store.Object, client.Object, warn: lines.Add).ComposeAsync(
            new MessageRequest("a line", "Three tasks are on the list.", Seat: "checklist-coach"), "", CancellationToken.None);

        Assert.Equal("Twelve things, and Margot is waiting.", reply);
        var line = Assert.Single(lines);
        Assert.StartsWith("VOICE UNGROUNDED checklist-coach", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Margot", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Twelve", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGroundedReplyLogsNothing()
    {
        var store = new Mock<IVoiceProfileStore>();
        store.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(VoiceProfile.Empty with { Register = "dry", DocumentsAnalyzed = 1 });
        var client = new Mock<IVoiceClient>();
        client.Setup(c => c.ComposeAsync(It.IsAny<VoiceProfile>(), It.IsAny<MessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Three on the list. Begin anywhere.");
        var lines = new List<string>();

        await new VoicedMessenger(store.Object, client.Object, warn: lines.Add).ComposeAsync(
            new MessageRequest("a line", "Three tasks are on the list.", Seat: "checklist-coach"), "", CancellationToken.None);

        Assert.Empty(lines);
    }
}
