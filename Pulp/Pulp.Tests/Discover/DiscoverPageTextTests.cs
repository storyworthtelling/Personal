using Pulp.Domain.Discover;
using Pulp.Domain.Voice;

namespace Pulp.Tests.Discover;

/// <summary>
/// Discover's quiet status sentences (DESIGN.md §5: loading is a sentence, not a spinner). "No idea"
/// from the model is one quiet sentence whatever the reason, never an error and never voiced (D99, G12):
/// the reason goes to the diagnostics log, not the page.
/// </summary>
public class DiscoverPageTextTests
{
    [Fact]
    public void TheStackLineIsMockupThrees() =>
        Assert.Equal("This week's stack · until Friday", DiscoverPageText.StackStatus);

    [Fact]
    public void EveryKindOfNoIdeaReadsAsTheSameQuietSentence()
    {
        var reasons = Enum.GetValues<MoodOutcome>().Where(outcome => outcome != MoodOutcome.Answered).ToList();
        Assert.NotEmpty(reasons);

        var sentences = reasons.Select(DiscoverPageText.MoodNoIdea).Distinct().ToList();

        var sentence = Assert.Single(sentences);
        Assert.DoesNotContain("error", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key", sentence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnAnswerIsNotNoIdea() =>
        Assert.Throws<ArgumentException>(() => DiscoverPageText.MoodNoIdea(MoodOutcome.Answered));

    [Fact]
    public void DiscoverHasNoVoicedSeatForSearchSoNoIdeaCannotBeVoiced()
    {
        var seats = VoicedOccasions.All.Select(occasion => occasion.Seat.ToString()).ToList();
        Assert.NotEmpty(seats);

        Assert.DoesNotContain(seats, seat => seat.Contains("Mood", StringComparison.Ordinal) || seat.Contains("Search", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadingSentencesNameWhatIsBeingLookedFor()
    {
        Assert.Equal("Looking for “glass”…", DiscoverPageText.Searching("glass"));
        Assert.Equal("Asking for “rainy and slow”…", DiscoverPageText.Asking("rainy and slow"));
        Assert.Equal("Nothing called “glass”.", DiscoverPageText.NoTitles("glass"));
    }
}
