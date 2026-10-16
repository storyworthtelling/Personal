using Pulp.Domain.Core;
using Pulp.Domain.Usage;

namespace Pulp.Tests.Usage;

/// <summary>What waits for the debounced Drive write (D320): the latest total per service-month, and a failed write put back without overwriting anything newer.</summary>
public class IndexUsageFileQueueTests
{
    private static IndexUsagePayload Payload(string service, int month, int count) =>
        new(new ServiceId(service), new UsageMonth(2026, month), count, null);

    [Fact]
    public void TheLatestTotalForAServiceMonthWins()
    {
        var queue = new IndexUsageFileQueue();
        queue.Add([Payload("netflix", 9, 3)]);
        queue.Add([Payload("netflix", 9, 4), Payload("hulu", 9, 1)]);

        Assert.Equal([("hulu", 1), ("netflix", 4)], queue.Drain().Select(p => (p.Service.Value, p.HandOffs)).OrderBy(x => x.Item1));
    }

    [Fact]
    public void DrainingEmptiesTheQueue()
    {
        var queue = new IndexUsageFileQueue();
        queue.Add([Payload("netflix", 9, 3)]);
        queue.Drain();

        Assert.False(queue.HasWork);
        Assert.Empty(queue.Drain());
    }

    [Fact]
    public void AFailedWriteIsPutBackButNeverOverANewerTotal()
    {
        var queue = new IndexUsageFileQueue();
        var drained = new[] { Payload("netflix", 9, 3), Payload("hulu", 9, 1) };
        queue.Add(drained);
        queue.Drain();
        queue.Add([Payload("netflix", 9, 5)]);

        queue.PutBack(drained);

        Assert.Equal([("hulu", 1), ("netflix", 5)], queue.Drain().Select(p => (p.Service.Value, p.HandOffs)).OrderBy(x => x.Item1));
    }
}
