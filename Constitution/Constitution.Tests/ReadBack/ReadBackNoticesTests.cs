using Codex.Domain.Design;
using Constitution.Domain.Notices;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Waiting;

namespace Constitution.Tests.ReadBack;

/// <summary>R3's notice: a page that came back unmarked, said plainly, with nothing to press.</summary>
public class ReadBackNoticesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 7, 15, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void ItIsRaisedOnceUnderItsOwnCause()
    {
        var board = new NoticeBoard();

        Assert.True(ReadBackNotices.NoPenMarks(board, Now));
        Assert.False(ReadBackNotices.NoPenMarks(board, Now.AddDays(1)));

        var notice = Assert.Single(board.Waiting());
        Assert.Equal(NoticeKind.NoPenMarks, notice.Kind);
        Assert.Equal(ReadBackNotices.NoPenMarksCause, notice.CauseKey);
        Assert.Equal(Now, notice.CreatedAt);
        Assert.Equal(FoldRole.Caution, notice.Role);
    }

    [Fact]
    public void AMarkTakesItAway()
    {
        var board = new NoticeBoard();
        ReadBackNotices.NoPenMarks(board, Now);

        Assert.True(ReadBackNotices.PenMarksSeen(board));
        Assert.Empty(board.Waiting());
        Assert.False(ReadBackNotices.PenMarksSeen(board));
    }

    [Fact]
    public void TheWaitingRowSaysWhatHappenedAndHasNothingToPress()
    {
        var board = new NoticeBoard();
        ReadBackNotices.NoPenMarks(board, Now);

        var item = Assert.Single(WaitingPageModel.From(board, Now).Items);

        Assert.Equal(FoldRole.Caution, item.Role);
        Assert.Null(item.Button);
        Assert.Empty(item.Chips);
        Assert.Contains("no pen marks", item.Title, StringComparison.Ordinal);
        Assert.Contains("keeps the page", item.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void ItNeverAsksAChipPairOrCallsItAnError()
    {
        var board = new NoticeBoard();
        ReadBackNotices.NoPenMarks(board, Now);

        var item = Assert.Single(WaitingPageModel.From(board, Now).Items);

        Assert.DoesNotContain("error", item.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", item.Body, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(FoldRole.Problem, item.Role);
    }
}
