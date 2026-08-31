using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class ShufflePlannerTests
{
    private static IReadOnlyList<Track> Tracks(int count) =>
        [.. Enumerable.Range(1, count).Select(n =>
            new Track($"{n}", $"T{n}", "A", "Album", "A", n, 1, "Rock", TimeSpan.FromMinutes(3)))];

    [Fact]
    public void Shuffle_KeepsEveryTrackExactlyOnce()
    {
        var input = Tracks(20);

        var shuffled = new ShufflePlanner(new Random(1)).Shuffle(input);

        Assert.Equal(
            input.Select(track => track.Id).OrderBy(id => id),
            shuffled.Select(track => track.Id).OrderBy(id => id));
    }

    [Fact]
    public void Shuffle_IsDeterministicForAGivenSeed()
    {
        var input = Tracks(20);

        var a = new ShufflePlanner(new Random(42)).Shuffle(input).Select(track => track.Id);
        var b = new ShufflePlanner(new Random(42)).Shuffle(input).Select(track => track.Id);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Shuffle_ActuallyReordersLargeInput()
    {
        var input = Tracks(30);

        var shuffled = new ShufflePlanner(new Random(7)).Shuffle(input);

        Assert.NotEqual(input.Select(track => track.Id), shuffled.Select(track => track.Id));
    }

    [Fact]
    public void Shuffle_HandlesEmptyAndSingle()
    {
        var planner = new ShufflePlanner(new Random(1));

        Assert.Empty(planner.Shuffle([]));
        Assert.Single(planner.Shuffle(Tracks(1)));
    }
}
