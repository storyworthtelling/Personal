using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

public class UnreadNoticesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 14, 0, 0, TimeSpan.Zero);

    private static (string Name, long SavedSeconds) File(string name, TimeSpan age) =>
        (name, Now.Subtract(age).ToUnixTimeSeconds());

    [Fact]
    public void KeepsRecentNoticesAndDiscardsThoseOlderThanTheCarrierWouldHold()
    {
        var plan = UnreadNotices.Plan([File("fresh", TimeSpan.FromHours(2)), File("stale", TimeSpan.FromDays(8))], Now);

        Assert.Equal(["fresh"], plan.Keep);
        Assert.Equal(["stale"], plan.Discard);
    }

    [Fact]
    public void KeepsOnlyTheNewestWhenThereAreTooMany()
    {
        var files = Enumerable.Range(0, 5).Select(i => File($"n{i}", TimeSpan.FromHours(i + 1))).ToList();

        var plan = UnreadNotices.Plan(files, Now, maxKept: 3);

        Assert.Equal(["n0", "n1", "n2"], plan.Keep);
        Assert.Equal(["n3", "n4"], plan.Discard);
    }

    [Fact]
    public void NameCarriesTheSaveTimeAndReadsBack()
    {
        var name = UnreadNotices.NameFor(Now, "abc");

        Assert.Equal(Now.ToUnixTimeSeconds(), UnreadNotices.SavedSeconds(name));
        Assert.Null(UnreadNotices.SavedSeconds("garbage.pdu"));
    }
}
