using Constitution.Domain.Meals;
using Constitution.Domain.Workouts;
using Constitution.Tests.MealTaste;
using static Constitution.Tests.MealTaste.TasteData;

namespace Constitution.Tests.ClaudeTasteJudge;

public class BoundedTasteJudgeTests
{
    private static TasteEvidence Words(string words) => new(Sam, TasteEvidenceKind.Denial, words, Pasta, Week);

    private static TasteJudgement Unavailable(AuthorUnavailable reason) => new TasteJudgement.Unavailable(reason);

    [Fact]
    public async Task ItAsksNoMoreThanItsLimit()
    {
        var inner = new FakeTasteJudge(_ => new TasteJudgement.OneOff());
        var bounded = new BoundedTasteJudge(inner, 2);

        var results = new List<TasteJudgement>();
        foreach (var words in new[] { "a", "b", "c", "d" })
        {
            results.Add(await bounded.JudgeAsync(Words(words), CancellationToken.None));
        }

        Assert.Equal(2, inner.Asked.Count);
        Assert.Equal(2, bounded.Asked);
        Assert.IsType<TasteJudgement.OneOff>(results[1]);
        Assert.IsType<TasteJudgement.Unavailable>(results[2]);
        Assert.IsType<TasteJudgement.Unavailable>(results[3]);
    }

    [Theory]
    [InlineData(AuthorUnavailable.KeyRejected)]
    [InlineData(AuthorUnavailable.Offline)]
    [InlineData(AuthorUnavailable.TimedOut)]
    public async Task ItStopsAskingOnceTheServiceCannotAnswer(AuthorUnavailable reason)
    {
        var inner = new FakeTasteJudge(_ => Unavailable(reason));
        var bounded = new BoundedTasteJudge(inner, 6);

        var first = await bounded.JudgeAsync(Words("a"), CancellationToken.None);
        var second = await bounded.JudgeAsync(Words("b"), CancellationToken.None);

        Assert.Single(inner.Asked);
        Assert.Equal(reason, Assert.IsType<TasteJudgement.Unavailable>(first).Reason);
        Assert.Equal(reason, Assert.IsType<TasteJudgement.Unavailable>(second).Reason);
    }

    [Fact]
    public async Task AnUnusableReplyToOneDoesNotStopTheOthers()
    {
        var inner = new FakeTasteJudge(evidence => evidence.Words == "a" ? Unavailable(AuthorUnavailable.ReplyUnusable) : new TasteJudgement.OneOff());
        var bounded = new BoundedTasteJudge(inner, 6);

        await bounded.JudgeAsync(Words("a"), CancellationToken.None);
        var second = await bounded.JudgeAsync(Words("b"), CancellationToken.None);

        Assert.Equal(2, inner.Asked.Count);
        Assert.IsType<TasteJudgement.OneOff>(second);
    }

    [Fact]
    public async Task NoKeyIsSeenAndStopsTheAsking()
    {
        var inner = new FakeTasteJudge(_ => Unavailable(AuthorUnavailable.NotConfigured));
        var bounded = new BoundedTasteJudge(inner, 6);

        await bounded.JudgeAsync(Words("a"), CancellationToken.None);
        await bounded.JudgeAsync(Words("b"), CancellationToken.None);

        Assert.True(bounded.SawNoKey);
        Assert.Single(inner.Asked);
    }

    [Fact]
    public async Task ADeferredAnswerSaysWhyAndIsNeverALesson()
    {
        var bounded = new BoundedTasteJudge(new FakeTasteJudge(_ => new TasteJudgement.OneOff()), 0);

        var left = Assert.IsType<TasteJudgement.Unavailable>(await bounded.JudgeAsync(Words("a"), CancellationToken.None));

        Assert.False(string.IsNullOrWhiteSpace(left.Detail));
        Assert.Equal(0, bounded.Asked);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        var bounded = new BoundedTasteJudge(new FakeTasteJudge(_ => new TasteJudgement.OneOff()), 6);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bounded.JudgeAsync(Words("a"), source.Token));
    }

    // ---- the week's bound ----

    [Fact]
    public void AReachedPassWithWordsLeftCountsOne()
    {
        Assert.Equal((1, true), TasteJudgeLimits.After(0, true, 2));
        Assert.Equal((3, true), TasteJudgeLimits.After(2, true, 1));
    }

    [Fact]
    public void APassThatNeverReachedTheJudgeCountsNothing()
    {
        Assert.Equal((4, true), TasteJudgeLimits.After(4, false, 2));
    }

    [Fact]
    public void ThePassThatUsesTheLastAttemptGivesUp()
    {
        Assert.Equal((TasteJudgeLimits.MostAttempts, false), TasteJudgeLimits.After(TasteJudgeLimits.MostAttempts - 1, true, 1));
    }

    [Fact]
    public void NothingUnjudgedIsNothingPending()
    {
        Assert.Equal((2, false), TasteJudgeLimits.After(2, true, 0));
        Assert.Equal((0, false), TasteJudgeLimits.After(0, false, 0));
    }
}
