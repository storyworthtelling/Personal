using Pulp.Domain.Discover;

namespace Pulp.Tests.Discover;

/// <summary>
/// The search field under Discover's header (D170, D171, D209): empty shows the stack, typing replaces
/// it in place; Title searches after a pause in typing (time passed in, so the debounce is a decision
/// rather than a timer); Mood is explicit and asks only when the owner says so, because every ask is a
/// model call.
/// </summary>
public class DiscoverPageInputTests
{
    private static readonly DateTimeOffset Typed = new(2026, 10, 14, 20, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyFieldShowsTheStackInEitherMode(string? text)
    {
        Assert.Equal(DiscoverPageAction.ShowStack, DiscoverPageInput.Decide(text, DiscoverSearchMode.Title, Typed, Typed, submitted: false).Action);
        Assert.Equal(DiscoverPageAction.ShowStack, DiscoverPageInput.Decide(text, DiscoverSearchMode.Mood, Typed, Typed, submitted: true).Action);
    }

    [Fact]
    public void ATitleWaitsUntilTheTypingPauses()
    {
        var step = DiscoverPageInput.Decide("glass", DiscoverSearchMode.Title, Typed, Typed.AddMilliseconds(100), submitted: false);

        Assert.Equal(DiscoverPageAction.Wait, step.Action);
        Assert.Equal(Typed + DiscoverPageInput.TitlePause, step.AskAgainAt);
    }

    [Fact]
    public void ATitleSearchesOnceThePauseHasPassed()
    {
        var step = DiscoverPageInput.Decide("  glass  ", DiscoverSearchMode.Title, Typed, Typed + DiscoverPageInput.TitlePause, submitted: false);

        Assert.Equal(DiscoverPageAction.SearchTitles, step.Action);
        Assert.Equal("glass", step.Text);
        Assert.Null(step.AskAgainAt);
    }

    [Fact]
    public void ASingleLetterIsNotSearchedOnAPauseButIsOnDone()
    {
        var paused = DiscoverPageInput.Decide("g", DiscoverSearchMode.Title, Typed, Typed.AddSeconds(5), submitted: false);
        var done = DiscoverPageInput.Decide("g", DiscoverSearchMode.Title, Typed, Typed, submitted: true);

        Assert.Equal(DiscoverPageAction.Wait, paused.Action);
        Assert.Null(paused.AskAgainAt);
        Assert.Equal(DiscoverPageAction.SearchTitles, done.Action);
    }

    [Fact]
    public void AMoodIsNeverAskedOnAPauseOnlyOnDone()
    {
        var paused = DiscoverPageInput.Decide("rainy and slow", DiscoverSearchMode.Mood, Typed, Typed.AddMinutes(5), submitted: false);
        var done = DiscoverPageInput.Decide("rainy and slow", DiscoverSearchMode.Mood, Typed, Typed, submitted: true);

        Assert.Equal(DiscoverPageAction.AwaitMoodDone, paused.Action);
        Assert.Null(paused.AskAgainAt);
        Assert.Equal(DiscoverPageAction.AskMood, done.Action);
        Assert.Equal("rainy and slow", done.Text);
    }

    [Fact]
    public void ATooLongMoodIsCutToWhatIsSent()
    {
        var rambling = new string('a', MoodQuery.MaxMoodLength + 40);
        var step = DiscoverPageInput.Decide(rambling, DiscoverSearchMode.Mood, Typed, Typed, submitted: true);

        Assert.Equal(MoodQuery.MaxMoodLength, step.Text.Length);
    }

    [Fact]
    public void TheChipsAreTitleThenMoodAsDrawnAndTheHintIsMockupThrees()
    {
        Assert.Equal(["Title", "Mood"], DiscoverPageInput.Modes.Select(DiscoverPageInput.Label).ToArray());
        Assert.Equal("Search titles and teams", DiscoverPageInput.Hint);
    }
}
