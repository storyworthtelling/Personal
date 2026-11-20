using Watch.Domain.Design;
using Watch.Domain.Touch;

namespace Watch.Tests.Touch;

public class HapticPlanTests
{
    private static readonly SessionState[] States = Enum.GetValues<SessionState>();

    [Fact]
    public void FourPatternsOneMeaningEach()
    {
        var meanings = new Dictionary<HapticPattern, HashSet<WatchEvent>>();
        foreach (var happened in Enum.GetValues<WatchEvent>())
        {
            foreach (var state in States)
            {
                var decision = HapticPlan.For(happened, state);
                if (decision.Pattern is { } pattern)
                {
                    meanings.TryAdd(pattern, []);
                    meanings[pattern].Add(happened);
                }
            }
        }

        Assert.Equal(4, meanings.Count);
        Assert.All(meanings.Values, events => Assert.Single(events));
        Assert.All(WatchTokens.Haptics.All, pattern => Assert.Contains(pattern, meanings.Keys));
    }

    [Fact]
    public void MeaningsMatchTheTable()
    {
        Assert.Equal(WatchEvent.Call, HapticPlan.MeaningOf(WatchTokens.Haptics.Call));
        Assert.Equal(WatchEvent.Text, HapticPlan.MeaningOf(WatchTokens.Haptics.Text));
        Assert.Equal(WatchEvent.RestEnded, HapticPlan.MeaningOf(WatchTokens.Haptics.RestEnd));
        Assert.Equal(WatchEvent.EaseOff, HapticPlan.MeaningOf(WatchTokens.Haptics.EaseOff));
    }

    [Fact]
    public void EveryPatternMeansWhatItIsUsedFor()
    {
        foreach (var happened in Enum.GetValues<WatchEvent>())
        {
            foreach (var state in States)
            {
                if (HapticPlan.For(happened, state).Pattern is { } pattern)
                {
                    Assert.Equal(happened, HapticPlan.MeaningOf(pattern));
                }
            }
        }
    }

    [Fact]
    public void TextsWaitForRest()
    {
        Assert.Equal(new HapticDecision(HapticAction.Hold, null), HapticPlan.For(WatchEvent.Text, SessionState.InSet));

        var released = HapticPlan.Moved(SessionState.InSet, SessionState.Resting, heldTexts: 3);

        Assert.Equal(HapticAction.Buzz, released.Action);
        Assert.Same(WatchTokens.Haptics.Text, released.Pattern);
    }

    [Fact]
    public void HeldTextsBuzzOnce()
    {
        var released = HapticPlan.Moved(SessionState.InSet, SessionState.Resting, heldTexts: 5);

        Assert.Same(WatchTokens.Haptics.Text, released.Pattern);
        Assert.False(released.Pattern!.Repeats);
    }

    [Fact]
    public void NothingHeldNothingReleased() =>
        Assert.Equal(HapticAction.Silent, HapticPlan.Moved(SessionState.InSet, SessionState.Resting, heldTexts: 0).Action);

    [Fact]
    public void SessionEndReleasesToo() =>
        Assert.Same(WatchTokens.Haptics.Text, HapticPlan.Moved(SessionState.InSet, SessionState.None, heldTexts: 1).Pattern);

    [Fact]
    public void StartingASetReleasesNothing() =>
        Assert.Equal(HapticAction.Silent, HapticPlan.Moved(SessionState.Resting, SessionState.InSet, heldTexts: 2).Action);

    [Theory]
    [InlineData(SessionState.None)]
    [InlineData(SessionState.Resting)]
    public void TextsBuzzOutsideASet(SessionState state) =>
        Assert.Same(WatchTokens.Haptics.Text, HapticPlan.For(WatchEvent.Text, state).Pattern);

    [Fact]
    public void MailIsSilent()
    {
        foreach (var state in States)
        {
            Assert.Equal(new HapticDecision(HapticAction.Silent, null), HapticPlan.For(WatchEvent.Mail, state));
        }
    }

    [Fact]
    public void CallsComeThroughASet()
    {
        foreach (var state in States)
        {
            Assert.Same(WatchTokens.Haptics.Call, HapticPlan.For(WatchEvent.Call, state).Pattern);
        }
    }

    [Fact]
    public void RestEndOnlyInRest()
    {
        Assert.Same(WatchTokens.Haptics.RestEnd, HapticPlan.For(WatchEvent.RestEnded, SessionState.Resting).Pattern);
        Assert.Equal(HapticAction.Silent, HapticPlan.For(WatchEvent.RestEnded, SessionState.InSet).Action);
        Assert.Equal(HapticAction.Silent, HapticPlan.For(WatchEvent.RestEnded, SessionState.None).Action);
    }

    [Fact]
    public void EaseOffAlwaysBuzzes()
    {
        foreach (var state in States)
        {
            Assert.Same(WatchTokens.Haptics.EaseOff, HapticPlan.For(WatchEvent.EaseOff, state).Pattern);
        }
    }
}
