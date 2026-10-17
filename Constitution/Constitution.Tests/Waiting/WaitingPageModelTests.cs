using Codex.Domain.Design;
using Constitution.Domain.Notices;
using Constitution.Domain.Waiting;

namespace Constitution.Tests.Waiting;

/// <summary>
/// The Waiting page's content (book p.18, C-9.1.2), decided with no view: a snapshot of the notice
/// board becomes rows, newest first, each with a role, plain copy, a relative date and at most one
/// button; a low-confidence read carries its two chips instead, and a pick is the answer.
/// </summary>
public class WaitingPageModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewestFirst()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.BoxUnderTen, "box", Now.AddDays(-3));
        board.Raise(NoticeKind.ReMarkableDelivery, "credential:remarkable", Now.AddHours(-1));
        board.Raise(NoticeKind.ScanToCheck, "scan", Now.AddDays(-1));

        var page = WaitingPageModel.From(board, Now);

        Assert.Equal(
            [NoticeKind.ReMarkableDelivery, NoticeKind.ScanToCheck, NoticeKind.BoxUnderTen],
            page.Items.Select(item => item.Kind).ToList());
    }

    [Fact]
    public void AtMostOneButton()
    {
        var board = new NoticeBoard();
        foreach (var kind in NoticeKinds.All)
        {
            board.Raise(kind, "cause:" + kind, Now);
        }

        var page = WaitingPageModel.From(board, Now);

        Assert.Equal(NoticeKinds.All.Count, page.Items.Count);
        foreach (var item in page.Items)
        {
            // The type allows only one button; a chip row replaces it rather than joining it.
            Assert.True(item.Chips.Count == 0 || item.Button is null, item.Kind + " has both chips and a button.");
        }
    }

    [Fact]
    public void AChipIsTheAnswer()
    {
        var board = new NoticeBoard();
        var cause = LowConfidenceCause.Key("push-ups", "18", "13");
        board.Raise(NoticeKind.LowConfidenceRead, cause, Now);
        var item = Assert.Single(WaitingPageModel.From(board, Now).Items);

        Assert.Equal(["18", "13"], item.Chips.Select(chip => chip.Value).ToList());
        Assert.Equal("Was it 18 or 13 push-ups?", item.Title);

        var answer = WaitingPageModel.Answer(board, item, item.Chips[1]);

        Assert.Equal(new WaitingAnswer(cause, "13"), answer);
        Assert.Empty(board.Waiting());
        Assert.Empty(WaitingPageModel.From(board, Now).Items);
    }

    [Fact]
    public void AValueThatIsNotAChipIsNotAnAnswer()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.LowConfidenceRead, LowConfidenceCause.Key("push-ups", "18", "13"), Now);
        var item = Assert.Single(WaitingPageModel.From(board, Now).Items);

        Assert.Throws<ArgumentException>(() => WaitingPageModel.Answer(board, item, new ChipChoice("20")));
        Assert.Single(board.Waiting());
    }

    [Fact]
    public void ARowWithNoChipsCannotBeAnswered()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.ScanToCheck, "scan", Now);
        var item = Assert.Single(WaitingPageModel.From(board, Now).Items);

        Assert.Throws<ArgumentException>(() => WaitingPageModel.Answer(board, item, new ChipChoice("18")));
    }

    [Fact]
    public void ALowConfidenceReadWithNoReadableCandidatesStillListsWithoutChips()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.LowConfidenceRead, "unparseable", Now);

        var item = Assert.Single(WaitingPageModel.From(board, Now).Items);

        Assert.Empty(item.Chips);
        Assert.Equal(FoldRole.Caution, item.Role);
        Assert.False(string.IsNullOrWhiteSpace(item.Title));
    }

    [Theory]
    [InlineData("", "18", "13")]
    [InlineData("push-ups", "", "13")]
    [InlineData("push:ups", "18", "13")]
    [InlineData("push-ups", "1|8", "13")]
    public void ACauseKeyRefusesPartsItCouldNotReadBack(string subject, string first, string second)
    {
        Assert.Throws<ArgumentException>(() => LowConfidenceCause.Key(subject, first, second));
    }

    [Fact]
    public void ACauseKeyReadsBackWhatWasPut()
    {
        var parsed = LowConfidenceCause.TryParse(LowConfidenceCause.Key("push-ups", "18", "13"));

        Assert.Equal(new LowConfidenceCause("push-ups", "18", "13"), parsed);
        Assert.Null(LowConfidenceCause.TryParse("credential:remarkable"));
    }

    [Theory]
    [InlineData(NoticeKind.ReMarkableDelivery, WaitingActionId.PairAgain, "Pair again", FoldRole.Problem)]
    [InlineData(NoticeKind.BedOffline, WaitingActionId.SignInAgain, "Sign in again", FoldRole.Problem)]
    [InlineData(NoticeKind.HealthConnectRevoked, WaitingActionId.ReconnectHealth, "Allow again", FoldRole.Problem)]
    [InlineData(NoticeKind.ScanToCheck, WaitingActionId.CheckCard, "Check it", FoldRole.Progress)]
    [InlineData(NoticeKind.BoxUnderTen, WaitingActionId.ScanCard, "Scan a card", FoldRole.Reference)]
    [InlineData(NoticeKind.KeepHandoff, WaitingActionId.SendToKeep, "Send to Keep", FoldRole.Reference)]
    public void EachKindHasItsFixedButtonAndRole(NoticeKind kind, WaitingActionId action, string label, FoldRole role)
    {
        var board = new NoticeBoard();
        board.Raise(kind, "cause", Now);

        var item = Assert.Single(WaitingPageModel.From(board, Now).Items);

        Assert.Equal(new WaitingButton(label, action), item.Button);
        Assert.Equal(role, item.Role);
        Assert.False(string.IsNullOrWhiteSpace(item.Title));
        Assert.False(string.IsNullOrWhiteSpace(item.Body));
    }

    [Fact]
    public void TheReMarkableRowSaysWhatHappenedAndWhatToDo()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.ReMarkableDelivery, "credential:remarkable", Now);

        var item = Assert.Single(WaitingPageModel.From(board, Now).Items);

        Assert.Equal("The reMarkable didn’t take this morning’s workout", item.Title);
        Assert.Equal(
            "Its sign-in was refused. Pair the tablet again; the new token goes to the vault and Constitution resends.",
            item.Body);
    }

    [Fact]
    public void ARowPrintsItsDateInTheSuitesRelativeRule()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.ScanToCheck, "scan", Now.AddDays(-1));

        var item = Assert.Single(WaitingPageModel.From(board, Now).Items);

        Assert.Equal("yesterday", item.DateText);
    }

    [Fact]
    public void NothingWaitingIsAnEmptyPage()
    {
        var page = WaitingPageModel.From(new NoticeBoard(), Now);

        Assert.Empty(page.Items);
    }

    [Fact]
    public void EveryKindHasCopy()
    {
        // A new NoticeKind fails here until it has words.
        foreach (var kind in NoticeKinds.All)
        {
            var board = new NoticeBoard();
            board.Raise(kind, "cause", Now);
            var item = Assert.Single(WaitingPageModel.From(board, Now).Items);
            Assert.Equal(NoticeKinds.RoleOf(kind), item.Role);
        }
    }
}
