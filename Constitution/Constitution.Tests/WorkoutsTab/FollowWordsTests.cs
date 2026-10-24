using Constitution.Domain.Workouts.Screens;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>
/// S2: the calendar is asked to follow a swap or a skip, and the answer used to be thrown away, so a refused edit left the event where it
/// was while the tab showed the change. Now every answer is read into a verdict and a plain sentence, and the tab says it: inline, under
/// its header, in words (DESIGN.md: a state is a sentence, not a toast).
/// </summary>
public class FollowWordsTests
{
    private static FollowReading Judge(params FollowOutcome[] outcomes) => FollowWords.Judge(outcomes);

    [Theory]
    [InlineData(FollowOutcome.Moved)]
    [InlineData(FollowOutcome.Removed)]
    [InlineData(FollowOutcome.NotOnCalendar)]
    public void WhatWentThroughNeedsNoWords(FollowOutcome outcome)
    {
        var reading = Judge(outcome);

        Assert.Equal(FollowVerdict.Settled, reading.Verdict);
        Assert.Null(reading.Sentence);
        Assert.False(reading.WillRetry);
    }

    [Fact]
    public void ASwapThatMovedBothNeedsNoWords()
    {
        Assert.Null(Judge(FollowOutcome.Moved, FollowOutcome.Moved).Sentence);
        Assert.Null(Judge(FollowOutcome.Moved, FollowOutcome.NotOnCalendar).Sentence);
    }

    [Fact]
    public void NothingToJudgeIsSettled()
    {
        Assert.Equal(FollowVerdict.Settled, Judge().Verdict);
    }

    [Theory]
    [InlineData(FollowOutcome.Failed)]
    [InlineData(FollowOutcome.Unavailable)]
    public void ARefusedOrUnreachableCalendarSaysItDidNotChangeAndWillTryAgain(FollowOutcome outcome)
    {
        var reading = Judge(outcome);

        Assert.Equal(FollowVerdict.NotChanged, reading.Verdict);
        Assert.Equal(FollowWords.CalendarUnchanged, reading.Sentence);
        Assert.True(reading.WillRetry);
    }

    [Fact]
    public void ASwapWhereNeitherEventMovedMayBeTriedAgain()
    {
        var reading = Judge(FollowOutcome.Unavailable, FollowOutcome.Unavailable);

        Assert.Equal(FollowVerdict.NotChanged, reading.Verdict);
        Assert.True(reading.WillRetry);
    }

    [Fact]
    public void ASwapWhereOnlyOneEventMovedIsNotTriedAgain()
    {
        // Moving the first onto the second's key and then trying the pair again would move the wrong event.
        var reading = Judge(FollowOutcome.Moved, FollowOutcome.Failed);

        Assert.Equal(FollowVerdict.PartlyChanged, reading.Verdict);
        Assert.Equal(FollowWords.CalendarPartlyChanged, reading.Sentence);
        Assert.False(reading.WillRetry);
    }

    [Fact]
    public void AnEventTheOwnerMovedIsSaidToBeLeftAlone()
    {
        var reading = Judge(FollowOutcome.OwnerHadMovedIt);

        Assert.Equal(FollowVerdict.LeftAlone, reading.Verdict);
        Assert.Equal(FollowWords.CalendarLeftAlone, reading.Sentence);
        Assert.False(reading.WillRetry);
    }

    [Fact]
    public void AMovedEventAndAnOwnerMovedOneIsLeftAloneToo()
    {
        Assert.Equal(FollowVerdict.LeftAlone, Judge(FollowOutcome.Moved, FollowOutcome.OwnerHadMovedIt).Verdict);
    }

    [Fact]
    public void AFailureOutranksAnOwnersMove()
    {
        var reading = Judge(FollowOutcome.OwnerHadMovedIt, FollowOutcome.Failed);

        Assert.Equal(FollowVerdict.NotChanged, reading.Verdict);
        Assert.True(reading.WillRetry);
    }

    [Fact]
    public void EverySentenceIsPlainWordsAndMakesNoClaimItCannotKeep()
    {
        string[] sentences =
        [
            FollowWords.SwapNotKept, FollowWords.SkipNotKept, FollowWords.CalendarUnchanged,
            FollowWords.CalendarPartlyChanged, FollowWords.CalendarLeftAlone, FollowWords.CalendarGaveUp,
        ];

        Assert.All(sentences, sentence =>
        {
            Assert.False(string.IsNullOrWhiteSpace(sentence));
            Assert.EndsWith(".", sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("!", sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("error", sentence, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("exception", sentence, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(sentences.Length, sentences.Distinct().Count());
        Assert.Contains("did not change", FollowWords.CalendarUnchanged, StringComparison.Ordinal);
        Assert.Contains("try again", FollowWords.CalendarUnchanged, StringComparison.Ordinal);
        Assert.Contains("as it was", FollowWords.SwapNotKept, StringComparison.Ordinal);
    }
}

/// <summary>S2: the line under the tab's header says what is wrong while something is, and the usual line when nothing is.</summary>
public class TabStatusLineTests
{
    [Fact]
    public void ANoticeTakesTheLineWhileThereIsOne()
    {
        Assert.Equal(FollowWords.CalendarUnchanged, WorkoutsTabModel.StatusWith(FollowWords.CalendarUnchanged));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoNoticeIsTheUsualLine(string? notice)
    {
        Assert.Equal(WorkoutsTabModel.StatusSentence, WorkoutsTabModel.StatusWith(notice));
    }
}
