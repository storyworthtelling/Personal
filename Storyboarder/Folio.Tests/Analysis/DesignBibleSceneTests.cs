using Folio.Domain.Analysis;
using Xunit;

namespace Folio.Tests.Analysis;

public class DesignBibleSceneTests
{
    private static readonly DesignBible Bible = new(
        [new EntityProfile("Aldric", "a knight", "tall", ["the Knight", "Sir A"], ChunkMentions: 9),
         new EntityProfile("Mara", "a healer", "average", [], ChunkMentions: 6),
         new EntityProfile("Zephyrine", "a queen", "average", [], ChunkMentions: 1),
         new EntityProfile("Brant", "a smith", "broad", [], ChunkMentions: 3)],
        [new EntityProfile("The Keep", "a tower", "huge", [], ChunkMentions: 7),
         new EntityProfile("Saltmarsh", "a flat", "vast", [], ChunkMentions: 2)]);

    [Fact]
    public void ForScene_KeepsEntitiesNamedInTheScene_IgnoringCase()
    {
        var scene = Bible.ForScene("aldric rode to THE KEEP.");

        Assert.Equal(["Aldric"], scene.Characters.Select(c => c.Name));
        Assert.Equal(["The Keep"], scene.Settings.Select(s => s.Name));
    }

    [Fact]
    public void ForScene_MatchesOnAnAlias()
    {
        var scene = Bible.ForScene("The Knight drew his sword.");

        Assert.Equal(["Aldric"], scene.Characters.Select(c => c.Name));
    }

    // A scene can speak only of "she" and "the room"; the model still needs somebody to draw, so the
    // book's most-mentioned cast stands in rather than sending nothing at all.
    [Fact]
    public void ForScene_FallsBackToTheMostMentionedWhenNothingIsNamed()
    {
        var scene = Bible.ForScene("She crossed the room and waited.");

        Assert.Equal(["Aldric", "Mara", "Brant"], scene.Characters.Select(c => c.Name));
        Assert.Equal(["The Keep", "Saltmarsh"], scene.Settings.Select(s => s.Name));
    }

    [Fact]
    public void Names_MatchesWholeWordsOnly()
    {
        Assert.False(DesignBible.Names("The banner hung in the cannon room.", "Ann"));
        Assert.True(DesignBible.Names("Ann waved.", "Ann"));
        Assert.True(DesignBible.Names("\u201cAnn,\u201d she said.", "Ann"));
    }
}
