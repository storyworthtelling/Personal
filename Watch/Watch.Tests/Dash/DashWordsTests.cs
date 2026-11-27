using Watch.Domain.Dash;
using Watch.Domain.Kit;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// Dash's fixed words (WD §6, A37): one short sentence at most, no exclamation; loading is a sentence that says what
/// is happening; success is silent; an error is a quiet line that says what went wrong; no app is named (A48).
/// </summary>
public class DashWordsTests
{
    private static IEnumerable<string> Sentences() =>
        new[] { DashWords.Finding, DashWords.Opening, DashWords.Sending, DashWords.TooLong }
            .Concat(Enum.GetValues<DashTrouble>().Select(DashWords.For))
            .Concat(Enum.GetValues<DashSendOutcome>().Select(DashWords.For).OfType<string>());

    private static IEnumerable<string> Labels() =>
        [DashWords.Reply, DashWords.ReplyToAll, DashWords.Send, DashWords.Keyboard, DashWords.Again, DashWords.To("Lee")];

    [Fact]
    public void EverySentenceIsOneSentence() =>
        Assert.All(Sentences(), sentence => Assert.Equal(WatchTextVerdict.Fits, WatchText.Check(sentence)));

    [Fact]
    public void EverySentenceEndsAsOne() =>
        Assert.All(Sentences(), sentence => Assert.EndsWith(".", sentence, StringComparison.Ordinal));

    [Fact]
    public void NothingShoutsOrUsesAStraightApostrophe() =>
        Assert.All(Sentences().Concat(Labels()), words =>
        {
            Assert.DoesNotContain("!", words, StringComparison.Ordinal);
            Assert.DoesNotContain("'", words, StringComparison.Ordinal);
        });

    [Fact]
    public void NoAppIsNamed() =>
        Assert.All(Sentences().Concat(Labels()), words =>
        {
            Assert.DoesNotContain("Dash", words, StringComparison.Ordinal);
            Assert.DoesNotContain("Post", words, StringComparison.Ordinal);
        });

    [Fact]
    public void SuccessIsSilent() => Assert.Null(DashWords.For(DashSendOutcome.Sent));

    [Fact]
    public void EveryOtherOutcomeIsSaidItsOwnWay()
    {
        var said = Enum.GetValues<DashSendOutcome>().Where(outcome => outcome != DashSendOutcome.Sent).Select(DashWords.For).ToList();

        Assert.All(said, Assert.NotNull);
        Assert.Equal(said.Count, said.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryTroubleIsSaidItsOwnWay()
    {
        var said = Enum.GetValues<DashTrouble>().Select(DashWords.For).ToList();

        Assert.NotEmpty(said);
        Assert.Equal(said.Count, said.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TheLabelsAreTheMockups()
    {
        Assert.Equal("Reply", DashWords.Reply);
        Assert.Equal("Reply to all", DashWords.ReplyToAll);
        Assert.Equal("Send", DashWords.Send);
        Assert.Equal("Keyboard", DashWords.Keyboard);
        Assert.Equal("Again", DashWords.Again);
        Assert.Equal("To Lee", DashWords.To("Lee"));
    }

    [Fact]
    public void ARowSaysWhoWhenAndWhat()
    {
        var unread = new PersonRow(LeeKey, "Lee", "Running late.", "2 min", Unread: true, Group: false);
        var read = unread with { Unread = false };

        Assert.Equal("Lee, new, 2 min: Running late.", DashWords.Row(unread));
        Assert.Equal("Lee, 2 min: Running late.", DashWords.Row(read));
    }
}
