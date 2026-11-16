using Folio.Domain.Analysis;
using Folio.Domain.Scenes;

namespace Folio.Tests.Scenes;

/// <summary>
/// Each scene the text contains is judged on its own (release A3–A4). The verdicts used to come from a
/// separate list the model made of its own idea of the scenes, matched to the real scenes by position, so
/// any difference in count printed every later verdict under the wrong scene.
/// </summary>
public class SceneJudgementTests
{
    /// <summary>The story so far for a scene is what the read had established before it began.</summary>
    [Fact]
    public void StorySoFarIsTheRunningStateOfTheLastBlockEndingBeforeTheScene()
    {
        var blocks = new[]
        {
            new TextBlockState(0, 1_000, "After block one."),
            new TextBlockState(1_000, 2_000, "After block two."),
            new TextBlockState(2_000, 3_000, "After block three."),
        };

        Assert.Equal(string.Empty, SceneContext.StorySoFar(blocks, sceneStart: 0));
        Assert.Equal(string.Empty, SceneContext.StorySoFar(blocks, sceneStart: 500));
        Assert.Equal("After block one.", SceneContext.StorySoFar(blocks, sceneStart: 1_000));
        Assert.Equal("After block two.", SceneContext.StorySoFar(blocks, sceneStart: 2_500));
    }

    [Theory]
    [InlineData(SceneCall.Keep, "Keep")]
    [InlineData(SceneCall.Tighten, "Tighten")]
    [InlineData(SceneCall.Rewrite, "Rewrite")]
    [InlineData(SceneCall.Cut, "Cut")]
    public void TheCallReadsAsOneWord(SceneCall call, string word) =>
        Assert.Equal(word, SceneWords.Call(call));

    [Theory]
    [InlineData(0.0, "Tension: low")]
    [InlineData(0.32, "Tension: low")]
    [InlineData(0.5, "Tension: medium")]
    [InlineData(0.67, "Tension: high")]
    [InlineData(1.0, "Tension: high")]
    public void TensionReadsAsABand(double tension, string words) =>
        Assert.Equal(words, SceneWords.Tension(tension));

    [Theory]
    [InlineData(SceneInconsistencyKind.Voice, "Voice")]
    [InlineData(SceneInconsistencyKind.Tense, "Tense")]
    [InlineData(SceneInconsistencyKind.Characterisation, "Characterisation")]
    [InlineData(SceneInconsistencyKind.Setting, "Setting")]
    public void AnInconsistencyIsLabelledByItsKind(SceneInconsistencyKind kind, string label) =>
        Assert.Equal(label, SceneWords.Kind(kind));
}
