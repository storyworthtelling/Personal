using Codex.Domain.Voice;
using Xunit;

namespace Codex.Tests.Voice;

public class VoicePickerTests
{
    private static readonly Dictionary<string, int> Even = new() { ["a"] = 1, ["b"] = 1 };

    [Fact]
    public void Pick_TakesTheOnlyCandidate()
    {
        Assert.Equal("a", VoicePicker.Pick(["a"], Even, lastCategory: null, roll: 0.5));
    }

    // The one rule that is not about weight: the voice must not say the same kind of
    // thing twice running, or a weighted draw reads as a rut.
    [Fact]
    public void Pick_DoesNotRepeatTheLastCategory()
    {
        Assert.Equal("b", VoicePicker.Pick(["a", "b"], Even, lastCategory: "a", roll: 0.1));
        Assert.Equal("b", VoicePicker.Pick(["a", "b"], Even, lastCategory: "a", roll: 0.9));
    }

    // Unless it is all there is. Silence would be a worse answer than a repeat.
    [Fact]
    public void Pick_RepeatsWhenThereIsNothingElse()
    {
        Assert.Equal("a", VoicePicker.Pick(["a"], Even, lastCategory: "a", roll: 0.5));
    }

    [Fact]
    public void Pick_FollowsTheWeights()
    {
        var weights = new Dictionary<string, int> { ["a"] = 9, ["b"] = 1 };

        Assert.Equal("a", VoicePicker.Pick(["a", "b"], weights, null, roll: 0.89));
        Assert.Equal("b", VoicePicker.Pick(["a", "b"], weights, null, roll: 0.91));
    }

    // A category that is not on offer lifts the others rather than producing silence:
    // the weights are shares of what is available, not of everything that exists.
    [Fact]
    public void Pick_RenormalizesOverWhatIsEligible()
    {
        var weights = new Dictionary<string, int> { ["a"] = 1, ["b"] = 1, ["absent"] = 98 };

        Assert.Equal("b", VoicePicker.Pick(["a", "b"], weights, null, roll: 0.6));
    }

    // A caller that has not set weights still gets a voice rather than nothing.
    [Fact]
    public void Pick_SpreadsEvenlyWhenNothingIsWeighted()
    {
        var none = new Dictionary<string, int>();

        Assert.Equal("a", VoicePicker.Pick(["a", "b"], none, null, roll: 0.1));
        Assert.Equal("b", VoicePicker.Pick(["a", "b"], none, null, roll: 0.9));
    }

    [Fact]
    public void Pick_OnNothingEligibleIsSilent()
    {
        Assert.Null(VoicePicker.Pick([], Even, null, 0.5));
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(2d)]
    public void Pick_SurvivesARollOutOfRange(double roll)
    {
        Assert.NotNull(VoicePicker.Pick(["a", "b"], Even, null, roll));
    }
}
