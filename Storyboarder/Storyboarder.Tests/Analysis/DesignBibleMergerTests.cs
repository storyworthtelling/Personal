using Storyboarder.Domain.Analysis;
using Xunit;

namespace Storyboarder.Tests.Analysis;

public class DesignBibleMergerTests
{
    private readonly DesignBibleMerger merger = new();

    private static DesignBibleFragment Characters(params EntityMention[] mentions) =>
        new(mentions, Array.Empty<EntityMention>());

    [Fact]
    public void EmptyFragments_ProduceEmptyBible()
    {
        var bible = merger.Merge([]);
        Assert.Empty(bible.Characters);
        Assert.Empty(bible.Settings);
    }

    [Fact]
    public void Dedupes_ByName_CaseInsensitive()
    {
        var bible = merger.Merge(
        [
            Characters(new EntityMention("Tom", "tall, red coat")),
            Characters(new EntityMention("TOM", "green eyes"))
        ]);

        var tom = Assert.Single(bible.Characters);
        Assert.Equal("Tom", tom.Name);
        Assert.Contains("tall, red coat", tom.CanonicalDescription);
        Assert.Contains("green eyes", tom.CanonicalDescription);
    }

    [Fact]
    public void Resolves_AliasesToCanonicalEntity()
    {
        var bible = merger.Merge(
        [
            Characters(new EntityMention("Thomas Reed", "weathered detective", ["Tom"])),
            Characters(new EntityMention("Tom", "smokes a pipe"))
        ]);

        var profile = Assert.Single(bible.Characters);
        Assert.Equal("Thomas Reed", profile.Name);
        Assert.Contains("weathered detective", profile.CanonicalDescription);
        Assert.Contains("smokes a pipe", profile.CanonicalDescription);
        Assert.Contains("Tom", profile.Aliases);
    }

    [Fact]
    public void DistinctEntities_AreKeptSeparate()
    {
        var bible = merger.Merge(
        [
            Characters(
                new EntityMention("Alice", "short"),
                new EntityMention("Bob", "tall"))
        ]);

        Assert.Equal(2, bible.Characters.Count);
    }

    [Fact]
    public void DuplicateDescriptions_AreNotRepeated()
    {
        var bible = merger.Merge(
        [
            Characters(new EntityMention("Alice", "short")),
            Characters(new EntityMention("Alice", "short"))
        ]);

        var alice = Assert.Single(bible.Characters);
        Assert.Equal("short", alice.CanonicalDescription);
    }

    [Fact]
    public void Merges_Scale_FirstNonEmptyWins()
    {
        // A later chunk usually re-mentions a character without restating their size, so the first
        // concrete scale seen is the one carried into the canonical profile.
        var bible = merger.Merge(
        [
            Characters(new EntityMention("Tom", "tall", "", [])),
            Characters(new EntityMention("Tom", "red coat", "about 1.8m tall", [])),
            Characters(new EntityMention("Tom", "green eyes", "roughly 2m", []))
        ]);

        var tom = Assert.Single(bible.Characters);
        Assert.Equal("about 1.8m tall", tom.Scale);
    }

    [Fact]
    public void Characters_And_Settings_AreMergedIndependently()
    {
        var bible = merger.Merge(
        [
            new DesignBibleFragment(
                [new EntityMention("Alice", "short")],
                [new EntityMention("The Tower", "tall stone keep")])
        ]);

        Assert.Single(bible.Characters);
        var setting = Assert.Single(bible.Settings);
        Assert.Equal("The Tower", setting.Name);
    }
}
