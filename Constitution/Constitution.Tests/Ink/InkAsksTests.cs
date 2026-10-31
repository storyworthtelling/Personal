using Constitution.Domain.Ink;
using Constitution.Domain.Notices;
using Constitution.Domain.Waiting;

namespace Constitution.Tests.Ink;

/// <summary>An item the resolver would not guess waits as a Caution notice with two chips (C-2.2.1).</summary>
public class InkAsksTests
{
    private readonly InkFixture fixture = new();
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 7, 0, 0, TimeSpan.Zero);

    private IReadOnlyList<InkResolution> AnAsk() =>
        fixture.Resolve(InkFixture.Page(InkFixture.Feel("feel-2", "feel-3")), InkFixture.Rating);

    [Fact]
    public void AnAskIsACautionNoticeWithTwoChips()
    {
        var board = new NoticeBoard();

        Assert.Equal(1, InkAsks.Raise(board, AnAsk(), Now));

        var item = Assert.Single(WaitingPageModel.From(board, Now).Items);
        Assert.Equal(NoticeKind.LowConfidenceRead, item.Kind);
        Assert.Equal(Codex.Domain.Design.FoldRole.Caution, item.Role);
        Assert.Equal(["right", "hard"], item.Chips.Select(c => c.Value));
        Assert.Null(item.Button);
    }

    [Fact]
    public void TheSameAskTwiceIsOneNotice()
    {
        var board = new NoticeBoard();
        InkAsks.Raise(board, AnAsk(), Now);

        Assert.Equal(0, InkAsks.Raise(board, AnAsk(), Now.AddHours(1)));
        Assert.Single(board.Waiting());
    }

    [Fact]
    public void ClearReadsRaiseNothing()
    {
        var board = new NoticeBoard();
        var page = InkFixture.Page(InkFixture.Empty("pushups"));

        Assert.Equal(0, InkAsks.Raise(board, fixture.Resolve(page, InkFixture.WorkoutBox), Now));
        Assert.Empty(board.Waiting());
    }

    [Fact]
    public void ANotReadPageRaisesNoQuestion()
    {
        // A failed read is the worker's Diagnostics line (R3), not a question for the owner.
        var board = new NoticeBoard();
        var resolutions = fixture.Resolve(InkPageRead.NotRead("down"), InkFixture.WorkoutBox);

        Assert.Equal(0, InkAsks.Raise(board, resolutions, Now));
    }

    [Fact]
    public void HandwritingWithSeparatorsStillMakesAKey()
    {
        // The cause key uses ':' and '|' as its own punctuation; a read of "1:2|3" must not break it.
        var page = InkFixture.Page(new InkTargetRead("reach", true, 0.5, "1:2|3", "4"));
        var ask = Assert.IsType<InkResolution.NeedsAsking>(fixture.One(page, InkFixture.Number));

        var parsed = LowConfidenceCause.TryParse(ask.CauseKey);
        Assert.NotNull(parsed);
        Assert.Equal("4", parsed!.Second);
        Assert.DoesNotContain(':', parsed.First);
        Assert.DoesNotContain('|', parsed.First);
        Assert.Equal(parsed.First, ask.First);
    }

    [Fact]
    public void TheNoticeCarriesTheCauseTheAskNamed()
    {
        var board = new NoticeBoard();
        var ask = Assert.IsType<InkResolution.NeedsAsking>(Assert.Single(AnAsk()));

        InkAsks.Raise(board, [ask], Now);

        Assert.Equal(ask.CauseKey, Assert.Single(board.Waiting()).CauseKey);
    }
}
