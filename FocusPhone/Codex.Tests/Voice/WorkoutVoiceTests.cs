using Codex.Domain.Voice;
using Xunit;

namespace Codex.Tests.Voice;

public class WorkoutVoiceTests
{
    // A weight for a category that is not offered, or a category with no weight, both
    // read as "never said" — a silent bug in the one place nobody would look.
    [Fact]
    public void Weights_CoverExactlyTheCategoriesOnOffer()
    {
        Assert.Equal(
            WorkoutVoice.Categories.OrderBy(name => name),
            WorkoutVoice.Weights.Keys.OrderBy(name => name));
    }

    [Fact]
    public void Weights_AreAllPositive()
    {
        Assert.All(WorkoutVoice.Weights.Values, weight => Assert.True(weight > 0));
    }

    [Fact]
    public void Purpose_SaysSomethingForEveryCategory()
    {
        Assert.All(WorkoutVoice.Categories, category =>
            Assert.False(string.IsNullOrWhiteSpace(WorkoutVoice.Purpose(category))));
    }

    // Mid-run is the worst possible moment to throw over a category name.
    [Fact]
    public void Purpose_FallsBackRatherThanFailing()
    {
        Assert.Equal(WorkoutVoice.Purpose(WorkoutVoice.Coaching), WorkoutVoice.Purpose("nonsense"));
    }
}
