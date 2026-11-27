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
        new[] { DashWords.Finding, DashWords.Opening, DashWords.Sending, DashWords.TooLong, DashWords.UndoLost, DashWords.OpeningPhoto }
            .Concat(Enum.GetValues<DashTrouble>().Select(DashWords.For))
            .Concat(Enum.GetValues<DashSendOutcome>().Select(DashWords.For).OfType<string>());

    private static IEnumerable<string> Labels() =>
        [
            DashWords.Reply, DashWords.ReplyToAll, DashWords.Send, DashWords.Keyboard, DashWords.Again, DashWords.To("Lee"), DashWords.File,
            DashWords.Delete, DashWords.TapToUndo, DashWords.Filed("Lee"), DashWords.Deleted("Lee"), DashWords.FileOrDelete,
            DashWords.Caption("Robin", "1 h"), DashWords.Photo("Robin", "1 h"),
        ];

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

    [Fact]
    public void TheActsWordsAreTheMockups()
    {
        // mock: Filed, with Undo; File and Delete are the choice before it (the phone's own words for the two acts).
        Assert.Equal("Lee filed", DashWords.Filed("Lee"));
        Assert.Equal("Lee deleted", DashWords.Deleted("Lee"));
        Assert.Equal("Tap to undo", DashWords.TapToUndo);
        Assert.Equal("File", DashWords.File);
        Assert.Equal("Delete", DashWords.Delete);
        Assert.Equal("File or delete", DashWords.FileOrDelete);
        Assert.Equal("The undo didn’t reach your phone.", DashWords.UndoLost);
    }

    [Fact]
    public void APhotosCaptionIsWhoAndWhen()
    {
        // mock: A group text with a photo ("Robin · 1 h"); the owner's own photo, or one in a person's conversation, says when.
        Assert.Equal("Robin · 1 h", DashWords.Caption("Robin", "1 h"));
        Assert.Equal("1 h", DashWords.Caption(null, "1 h"));
        Assert.Equal("Opening the photo.", DashWords.OpeningPhoto);
    }

    [Fact]
    public void APhotoSaysWhoseItIsToTalkBack()
    {
        Assert.Equal("Photo from Robin, 1 h", DashWords.Photo("Robin", "1 h"));
        Assert.Equal("Photo, 1 h", DashWords.Photo(null, "1 h"));
    }
}
