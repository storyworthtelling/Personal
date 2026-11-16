using Codex.Domain.Plate;
using Codex.Domain.Post;
using Codex.Domain.Voice;
using Xunit;

namespace Codex.Tests.Voice;

/// <summary>
/// A seat whose baseline is written in code asks the voice to say it the owner's way (audit W10.2): Post's
/// empty Mailbox and Plate's empty Darkroom. The baseline stays the fallback, so with no profile or key the
/// screen reads exactly as before.
/// </summary>
public class VoiceRewriteTests
{
    private static readonly Storyworthtelling.CallSheet.VoiceLine Mailbox =
        Storyworthtelling.CallSheet.CallSheet.For("Post", "empty-mailbox");

    [Fact]
    public void TheAskCarriesTheBaselineAndTheOccasion()
    {
        var ask = VoiceRewrite.Ask(Mailbox, "The desk is clear.");

        Assert.NotNull(ask);
        Assert.Same(Mailbox, ask!.Line);
        Assert.Contains("the Mailbox is empty", ask.Purpose);
        Assert.Contains("The desk is clear.", ask.Context);
    }

    [Fact]
    public void TheVoiceIsToldToKeepWhatTheLineMeans()
    {
        var ask = VoiceRewrite.Ask(Mailbox, "Still nothing. I did say I would notify you.")!;

        Assert.Contains("keep", ask.Purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no facts", ask.Purpose, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("…")]
    [InlineData("...")]
    [InlineData("  ")]
    public void ALineWithNoWordsIsShownAsItIs(string baseline)
    {
        // The ladder's last rung is a silence; there is nothing to say differently.
        Assert.Null(VoiceRewrite.Ask(Mailbox, baseline));
    }

    [Fact]
    public void EveryWordedMailboxLineIsVoiced()
    {
        foreach (var line in EmptyMailbox.Seeds.SelectMany(seed => seed.Rungs).Where(line => line.Any(char.IsLetter)))
        {
            var ask = EmptyMailbox.Ask(line);
            Assert.NotNull(ask);
            Assert.Contains(line, ask!.Context);
        }
    }

    [Fact]
    public void EveryDarkroomLineIsVoiced()
    {
        foreach (var line in DarkroomVoice.EmptyLines)
        {
            var ask = DarkroomVoice.Ask(line);
            Assert.NotNull(ask);
            Assert.Contains(line, ask!.Context);
        }
    }

    [Fact]
    public void TheTwoSeatsAreTwoOccasions()
    {
        Assert.NotEqual(EmptyMailbox.Ask("Nothing.")!.Purpose, DarkroomVoice.Ask("Nothing.")!.Purpose);
    }
}
