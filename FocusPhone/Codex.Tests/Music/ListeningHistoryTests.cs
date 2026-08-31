using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class ListeningHistoryTests
{
    private static readonly DateTimeOffset Base = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static ListeningEvent Event(string session, int minute) =>
        new("id", "Jazz", "A", "A", ListeningSignal.Completed, 12, session, Base.AddMinutes(minute));

    [Fact]
    public void Trim_UnderCap_ReturnsAll()
    {
        var events = new[] { Event("s1", 0), Event("s2", 1) };
        Assert.Equal(2, ListeningHistory.Trim(events, maxSessions: 5).Count);
    }

    [Fact]
    public void Trim_OverCap_DropsOldestSessions()
    {
        var events = new[]
        {
            Event("s1", 0), // oldest session
            Event("s2", 10),
            Event("s3", 20), // newest session
        };
        var kept = ListeningHistory.Trim(events, maxSessions: 2);
        Assert.DoesNotContain(kept, e => e.SessionId == "s1");
        Assert.Contains(kept, e => e.SessionId == "s2");
        Assert.Contains(kept, e => e.SessionId == "s3");
    }

    [Fact]
    public void Trim_KeepsEveryEventOfAKeptSession()
    {
        var events = new[]
        {
            Event("s1", 0),
            Event("s2", 10), Event("s2", 11), Event("s2", 12),
        };
        var kept = ListeningHistory.Trim(events, maxSessions: 1);
        Assert.Equal(3, kept.Count);
        Assert.All(kept, e => Assert.Equal("s2", e.SessionId));
    }

    [Fact]
    public void Trim_PreservesInputOrder()
    {
        var events = new[] { Event("s2", 10), Event("s1", 0), Event("s2", 11) };
        var kept = ListeningHistory.Trim(events, maxSessions: 2);
        Assert.Equal([10, 0, 11], kept.Select(e => (int)(e.AtUtc - Base).TotalMinutes).ToArray());
    }

    [Fact]
    public void Trim_NonPositiveCap_ReturnsEmpty()
    {
        var events = new[] { Event("s1", 0) };
        Assert.Empty(ListeningHistory.Trim(events, maxSessions: 0));
    }
}
