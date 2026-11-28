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
        new[]
            {
                DashWords.Finding, DashWords.Opening, DashWords.Sending, DashWords.TooLong, DashWords.UndoLost, DashWords.OpeningPhoto,
                DashWords.OpeningContacts, DashWords.NoContacts, DashWords.NoOneByThatName, DashWords.Keeping, DashWords.NoteTooLong,
                DashWords.FindingMail, DashWords.NoMail,
            }
            .Concat(Enum.GetValues<DashTrouble>().Select(DashWords.For))
            .Concat(Enum.GetValues<DashSendOutcome>().Select(DashWords.For).OfType<string>());

    private static IEnumerable<string> Labels() =>
        [
            DashWords.Reply, DashWords.ReplyToAll, DashWords.Send, DashWords.Keyboard, DashWords.Again, DashWords.To("Lee"), DashWords.File,
            DashWords.Delete, DashWords.TapToUndo, DashWords.Filed("Lee"), DashWords.Deleted("Lee"), DashWords.FileOrDelete,
            DashWords.Caption("Robin", "1 h"), DashWords.Photo("Robin", "1 h"), DashWords.NewText, DashWords.SayAName, DashWords.Everyone,
            DashWords.WhoTo, DashWords.AddANote, DashWords.NotNow, DashWords.NoteOnThisCall, DashWords.Keep, DashWords.CallWith("Robin"),
            DashWords.CallWith(null), DashWords.CallLine(TimeSpan.FromMinutes(6), "just now"), DashWords.CallLine(null, "just now"), DashWords.Mail,
            DashWords.NoSubject, DashWords.MailLine(new MailRow("m6666666666666666", "Saturday plans", "Sam", "20 min")),
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

    [Fact]
    public void TheRolodexsWordsAreItsOwn()
    {
        // B6 (no mockup draws it; nearest drawn: People waiting's foot entry, Mail's list, Replying by voice's choices).
        Assert.Equal("New text", DashWords.NewText);
        Assert.Equal("Say a name", DashWords.SayAName);
        Assert.Equal("Everyone", DashWords.Everyone);
        Assert.Equal("Who to?", DashWords.WhoTo);
        Assert.Equal("Opening your contacts.", DashWords.OpeningContacts);
        Assert.Equal("There’s nobody in your phone’s contacts.", DashWords.NoContacts);
        // The phone's own Rolodex says the same when a search finds nobody.
        Assert.Equal("No one by that name.", DashWords.NoOneByThatName);
        Assert.Equal("Your phone’s contacts kept changing.", DashWords.For(DashTrouble.Changing));
    }

    // ---- B7: After the call (mock), the note read back, and Mail (mock) ----

    [Fact]
    public void AfterTheCallIsTheMockups()
    {
        // mock: After the call ("Call with Robin", "6 min, just now", Add a note, Not now).
        Assert.Equal("Call with Robin", DashWords.CallWith("Robin"));
        Assert.Equal("6 min, just now", DashWords.CallLine(TimeSpan.FromMinutes(6), "just now"));
        Assert.Equal("Add a note", DashWords.AddANote);
        Assert.Equal("Not now", DashWords.NotNow);
    }

    [Fact]
    public void ACallerPostCannotNameIsACall()
    {
        // Post sends a name or nothing, never a number, so a caller its book doesn't know is just a call.
        Assert.Equal("A call", DashWords.CallWith(null));
        Assert.Equal("Just now", DashWords.CallLine(null, "just now"));
        Assert.Equal("2 min", DashWords.CallLine(null, "2 min"));
    }

    [Theory]
    [InlineData(20, "Under a minute")]
    [InlineData(60, "1 min")]
    [InlineData(370, "6 min")]
    [InlineData(3599, "59 min")]
    [InlineData(3600, "1 h")]
    [InlineData(4500, "1 h 15 min")]
    public void ACallsLengthIsShort(int seconds, string words)
    {
        Assert.Equal(words, DashWords.Length(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void TheNoteIsThePhonesOwnWords()
    {
        // No mockup draws the note read back; the phone's own call note says "Note on this call" and "Keep".
        Assert.Equal("Note on this call", DashWords.NoteOnThisCall);
        Assert.Equal("Keep", DashWords.Keep);
        Assert.Equal("Keeping the note.", DashWords.Keeping);
        Assert.Equal("That’s too long for one note.", DashWords.NoteTooLong);
    }

    [Fact]
    public void MailIsTheMockups()
    {
        Assert.Equal("Mail", DashWords.Mail);
        Assert.Equal("Finding your mail.", DashWords.FindingMail);
        Assert.Equal("No mail waiting.", DashWords.NoMail);
    }

    [Fact]
    public void AMailLineSaysWhatFromWhomAndWhen()
    {
        Assert.Equal("Saturday plans, from Sam, 20 min", DashWords.MailLine(new MailRow("m6666666666666666", "Saturday plans", "Sam", "20 min")));
    }
}
