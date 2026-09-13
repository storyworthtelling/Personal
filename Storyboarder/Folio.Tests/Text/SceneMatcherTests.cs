using Folio.Domain.Text;
using Xunit;

namespace Folio.Tests.Text;

// The pure cross-draft scene matcher: given the previous draft's scenes and this draft's scenes (each as
// text), it decides which new scene corresponds to which old one and whether the text changed — driving
// image reuse (unchanged → reuse the old render) and before/after pairs (changed → render fresh + pair).
public class SceneMatcherTests
{
    private readonly SceneMatcher matcher = new();

    private static SceneText Scene(int number, string text) => new(number, text);

    [Fact]
    public void IdenticalScenes_MatchUnchanged()
    {
        var previous = new[] { Scene(1, "The gate creaks open."), Scene(2, "She runs to the tower.") };
        var current = new[] { Scene(1, "The gate creaks open."), Scene(2, "She runs to the tower.") };

        var result = matcher.Match(previous, current);

        Assert.Collection(result,
            m => { Assert.Equal(1, m.CurrentNumber); Assert.Equal(1, m.PreviousNumber); Assert.False(m.Changed); },
            m => { Assert.Equal(2, m.CurrentNumber); Assert.Equal(2, m.PreviousNumber); Assert.False(m.Changed); });
    }

    [Fact]
    public void WhitespaceAndCaseOnlyDifference_StillCountsUnchanged()
    {
        var previous = new[] { Scene(1, "The gate creaks open.") };
        var current = new[] { Scene(1, "the   GATE\n creaks  open.") };

        var m = Assert.Single(matcher.Match(previous, current));
        Assert.Equal(1, m.PreviousNumber);
        Assert.False(m.Changed);
    }

    [Fact]
    public void RewordedScene_MatchesButIsChanged()
    {
        var previous = new[] { Scene(1, "The old iron gate creaks slowly open before her.") };
        // Same beat, heavily reworded but sharing most content words.
        var current = new[] { Scene(1, "The old iron gate creaks open before her, slowly and with a groan.") };

        var m = Assert.Single(matcher.Match(previous, current));
        Assert.Equal(1, m.PreviousNumber);
        Assert.True(m.Changed);
    }

    [Fact]
    public void InsertedScene_IsNew_AndOthersStillMatchByContent()
    {
        var previous = new[] { Scene(1, "The gate creaks open."), Scene(2, "She runs to the tower.") };
        // A brand-new scene is inserted in the middle; the surrounding scenes are unchanged but renumber.
        var current = new[]
        {
            Scene(1, "The gate creaks open."),
            Scene(2, "A stranger watches from the wall, unseen and patient."),
            Scene(3, "She runs to the tower.")
        };

        var result = matcher.Match(previous, current);

        var s1 = result.Single(m => m.CurrentNumber == 1);
        Assert.Equal(1, s1.PreviousNumber);
        Assert.False(s1.Changed);

        var inserted = result.Single(m => m.CurrentNumber == 2);
        Assert.Null(inserted.PreviousNumber);   // new scene — no counterpart
        Assert.False(inserted.Changed);

        var s3 = result.Single(m => m.CurrentNumber == 3);
        Assert.Equal(2, s3.PreviousNumber);     // matched the old scene 2 despite renumbering
        Assert.False(s3.Changed);
    }

    [Fact]
    public void RemovedScene_LeavesNoMatchForIt()
    {
        var previous = new[]
        {
            Scene(1, "The gate creaks open."),
            Scene(2, "A long digression about the weather that the author cut."),
            Scene(3, "She runs to the tower.")
        };
        var current = new[] { Scene(1, "The gate creaks open."), Scene(2, "She runs to the tower.") };

        var result = matcher.Match(previous, current);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result.Single(m => m.CurrentNumber == 1).PreviousNumber);
        Assert.Equal(3, result.Single(m => m.CurrentNumber == 2).PreviousNumber);
        // The removed old scene 2 simply never appears as a PreviousNumber.
        Assert.DoesNotContain(result, m => m.PreviousNumber == 2);
    }

    [Fact]
    public void CompletelyDifferentScene_IsNewNotAWeakMatch()
    {
        var previous = new[] { Scene(1, "The gate creaks open before the trembling girl.") };
        var current = new[] { Scene(1, "Sunlight floods a quiet kitchen where bread is baking.") };

        var m = Assert.Single(matcher.Match(previous, current));
        Assert.Null(m.PreviousNumber);   // below the similarity threshold — treated as a new scene
    }

    [Fact]
    public void FirstDraft_NoPreviousScenes_AllNew()
    {
        var current = new[] { Scene(1, "The gate creaks open."), Scene(2, "She runs.") };

        var result = matcher.Match([], current);

        Assert.All(result, m => Assert.Null(m.PreviousNumber));
        Assert.All(result, m => Assert.False(m.Changed));
    }
}
