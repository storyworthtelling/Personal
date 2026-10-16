using Pulp.Domain.Sports;

namespace Pulp.Tests.Sports;

public class HighlightVideoCacheTests
{
    private static readonly GameId Game = new("harbor-game-1");

    [Fact]
    public void AnEarlyNoneYetIsNotRemembered()
    {
        var cache = new HighlightVideoCache();

        cache.Remember(Game, null);

        Assert.False(cache.TryGet(Game, out _));
    }

    [Fact]
    public void AFoundVideoIsRemembered()
    {
        var cache = new HighlightVideoCache();

        cache.Remember(Game, "clip-example");

        Assert.True(cache.TryGet(Game, out var video));
        Assert.Equal("clip-example", video);
    }

    [Fact]
    public void ALaterFindReplacesNothingAndAMissNeverEvictsAFind()
    {
        var cache = new HighlightVideoCache();
        cache.Remember(Game, "clip-example");

        cache.Remember(Game, null);

        Assert.True(cache.TryGet(Game, out var video));
        Assert.Equal("clip-example", video);
    }

    [Fact]
    public async Task ConcurrentWritersAndReadersNeverThrow()
    {
        var cache = new HighlightVideoCache();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
            {
                var id = new GameId($"game-{i % 50}");
                cache.Remember(id, i % 3 == 0 ? null : $"clip-{worker}");
                cache.TryGet(id, out _);
            }
        })));

        Assert.True(cache.TryGet(new GameId("game-1"), out _));
    }
}
