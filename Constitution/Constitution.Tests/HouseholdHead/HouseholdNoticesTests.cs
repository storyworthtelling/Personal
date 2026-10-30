using Codex.Domain.Design;
using Constitution.Domain.Notices;
using Constitution.Domain.Waiting;
using Constitution.Infrastructure.Household;
using Storyworthtelling.Household;

namespace Constitution.Tests.HouseholdHead;

/// <summary>D109: a household file that will not open also waits behind the dog-ear, and goes when the file opens again.</summary>
public class HouseholdNoticesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);

    private static HouseholdStanding Blocked(HouseholdUnreadableReason reason) =>
        new(HouseholdFileState.Unreadable, null, HouseholdSentences.Unreadable(reason), reason);

    private static HouseholdStanding Of(HouseholdFileState state) => new(state, null, "x");

    [Fact]
    public void ABlockedFileRaisesTheProblemNotice()
    {
        var board = new NoticeBoard();

        Assert.True(HouseholdNotices.Apply(board, Blocked(HouseholdUnreadableReason.WrongPassphrase), Now));

        var notice = Assert.Single(board.Waiting());
        Assert.Equal(NoticeKind.HouseholdFileUnreadable, notice.Kind);
        Assert.Equal("household:blocked:wrongpassphrase", notice.CauseKey);
        Assert.Equal(FoldRole.Problem, notice.Role);
    }

    [Theory]
    [InlineData(HouseholdUnreadableReason.NoPassphrase, "nopassphrase")]
    [InlineData(HouseholdUnreadableReason.WrongPassphrase, "wrongpassphrase")]
    [InlineData(HouseholdUnreadableReason.Damaged, "damaged")]
    [InlineData(HouseholdUnreadableReason.NewerFormat, "newerformat")]
    public void EachReasonHasItsWordInTheKey(HouseholdUnreadableReason reason, string word)
    {
        Assert.Equal("household:blocked:" + word, HouseholdNotices.CauseKey(reason));
    }

    [Fact]
    public void AgainWithTheSameReasonChangesNothing()
    {
        var board = new NoticeBoard();
        HouseholdNotices.Apply(board, Blocked(HouseholdUnreadableReason.Damaged), Now);

        Assert.False(HouseholdNotices.Apply(board, Blocked(HouseholdUnreadableReason.Damaged), Now.AddMinutes(5)));

        Assert.Equal(Now, Assert.Single(board.Waiting()).CreatedAt);
    }

    [Fact]
    public void ANewReasonReplacesTheOldOneNotStacks()
    {
        var board = new NoticeBoard();
        HouseholdNotices.Apply(board, Blocked(HouseholdUnreadableReason.NoPassphrase), Now);

        Assert.True(HouseholdNotices.Apply(board, Blocked(HouseholdUnreadableReason.WrongPassphrase), Now.AddMinutes(1)));

        Assert.Equal("household:blocked:wrongpassphrase", Assert.Single(board.Waiting()).CauseKey);
    }

    [Fact]
    public void SyncedTakesTheNoticeAway()
    {
        var board = new NoticeBoard();
        HouseholdNotices.Apply(board, Blocked(HouseholdUnreadableReason.Damaged), Now);

        Assert.True(HouseholdNotices.Apply(board, Of(HouseholdFileState.Synced), Now.AddMinutes(1)));

        Assert.Empty(board.Waiting());
    }

    [Theory]
    [InlineData(HouseholdFileState.NotCompared)]
    [InlineData(HouseholdFileState.LocalOnly)]
    [InlineData(HouseholdFileState.Offline)]
    [InlineData(HouseholdFileState.WaitingForShare)]
    public void AStandingThatSaysNothingAboutTheFileLeavesItAlone(HouseholdFileState state)
    {
        var board = new NoticeBoard();
        HouseholdNotices.Apply(board, Blocked(HouseholdUnreadableReason.Damaged), Now);

        Assert.False(HouseholdNotices.Apply(board, Of(state), Now.AddMinutes(1)));

        Assert.Single(board.Waiting());
    }

    [Fact]
    public void SyncedWithNothingWaitingChangesNothing()
    {
        Assert.False(HouseholdNotices.Apply(new NoticeBoard(), Of(HouseholdFileState.Synced), Now));
    }

    [Fact]
    public void OtherNoticesAreUntouched()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.BedOffline, "credential:sleep-number", Now);
        HouseholdNotices.Apply(board, Blocked(HouseholdUnreadableReason.Damaged), Now);
        HouseholdNotices.Apply(board, Of(HouseholdFileState.Synced), Now);

        Assert.Equal(NoticeKind.BedOffline, Assert.Single(board.Waiting()).Kind);
    }

    [Fact]
    public void ABlockedStandingWithNoReasonStillRaisesOne()
    {
        var board = new NoticeBoard();

        HouseholdNotices.Apply(board, new HouseholdStanding(HouseholdFileState.Unreadable, null, "x"), Now);

        Assert.Equal("household:blocked:damaged", Assert.Single(board.Waiting()).CauseKey);
    }

    [Theory]
    [InlineData(HouseholdUnreadableReason.NoPassphrase, "no vault passphrase")]
    [InlineData(HouseholdUnreadableReason.WrongPassphrase, "doesn’t match")]
    [InlineData(HouseholdUnreadableReason.Damaged, "damaged")]
    [InlineData(HouseholdUnreadableReason.NewerFormat, "newer version")]
    public void TheWaitingRowSaysTheSpecificReason(HouseholdUnreadableReason reason, string phrase)
    {
        var board = new NoticeBoard();
        HouseholdNotices.Apply(board, Blocked(reason), Now);

        var item = Assert.Single(WaitingPageModel.From(board, Now).Items);

        Assert.Contains(phrase, item.Body, StringComparison.Ordinal);
        Assert.Equal("The household file won’t open", item.Title);
        Assert.Equal(FoldRole.Problem, item.Role);
        Assert.Equal(WaitingActionId.OpenSettings, item.Button?.Action);
        Assert.Equal("Open Settings", item.Button?.Label);
        Assert.Empty(item.Chips);
    }

    [Fact]
    public void ACauseWithNoKnownWordStillReadsPlainly()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.HouseholdFileUnreadable, "household:blocked", Now);

        var item = Assert.Single(WaitingPageModel.From(board, Now).Items);

        Assert.Contains("household file", item.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoKeyOrCopyCarriesASecret()
    {
        foreach (var reason in Enum.GetValues<HouseholdUnreadableReason>())
        {
            var board = new NoticeBoard();
            HouseholdNotices.Apply(board, Blocked(reason), Now);
            var notice = Assert.Single(board.Waiting());
            var item = Assert.Single(WaitingPageModel.From(board, Now).Items);

            Assert.Matches("^household:blocked:[a-z]+$", notice.CauseKey);
            Assert.DoesNotContain("{", item.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", item.Body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ARaiseIsFedFromTheHeadAfterEveryPass()
    {
        var head = File.ReadAllText(ConstitutionSource.Files(".cs").Single(file =>
            ConstitutionSource.Relative(file) == "Constitution.Android/Household/ConstitutionHousehold.cs"));

        Assert.Contains("ConnectionNoticeBoard.With", head, StringComparison.Ordinal);
        Assert.Contains("HouseholdNotices.Apply(board, standing", head, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadSendsTheOpenSettingsButtonToSettings()
    {
        var activity = File.ReadAllText(ConstitutionSource.Files(".cs").Single(file =>
            ConstitutionSource.Relative(file) == "Constitution.Android/ConstitutionActivity.cs"));

        Assert.Contains("WaitingActionId.OpenSettings", activity, StringComparison.Ordinal);
    }
}
