using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class CastQueuePlanTests
{
    private static Track T(string id) => new(id, "Title " + id, "Artist", "Album", "Artist", 1, 1, "Jazz", TimeSpan.FromMinutes(3));

    private static readonly IReadOnlyList<Track> Tracks = [T("a"), T("b"), T("c")];

    [Fact]
    public void Build_AllResolvable_KeepsOrder()
    {
        var plan = CastQueuePlan.Build(Tracks, t => $"https://host/{t.Id}");
        Assert.Empty(plan.Skipped);
        Assert.Equal(["a", "b", "c"], plan.Playable.Select(i => i.Track.Id).ToArray());
        Assert.Equal("https://host/b", plan.Playable[1].Url);
    }

    [Fact]
    public void Build_UnresolvableTracks_AreSkipped()
    {
        var plan = CastQueuePlan.Build(Tracks, t => t.Id == "b" ? null : $"https://host/{t.Id}");
        Assert.Equal(["a", "c"], plan.Playable.Select(i => i.Track.Id).ToArray());
        Assert.Equal(["b"], plan.Skipped.Select(t => t.Id).ToArray());
    }

    [Fact]
    public void Build_NoneResolvable_AllSkipped()
    {
        var plan = CastQueuePlan.Build(Tracks, _ => null);
        Assert.Empty(plan.Playable);
        Assert.Equal(3, plan.Skipped.Count);
    }

    [Fact]
    public void Build_EmptyStringUrl_IsSkipped()
    {
        var plan = CastQueuePlan.Build([T("a")], _ => "");
        Assert.Empty(plan.Playable);
        Assert.Single(plan.Skipped);
    }
}
