using Pulp.Domain.Core;
using Pulp.Domain.Usage;

namespace Pulp.Tests.Usage;

/// <summary>The decisions behind the debounced Drive write: a debounce cancel keeps the queue, a failure or timeout during the write puts it back, and two writes never overlap.</summary>
public class IndexUsageFileFlusherTests
{
    private static IndexUsagePayload Payload(string service, int month, int count) =>
        new(new ServiceId(service), new UsageMonth(2026, month), count, null);

    private static Task NoWait(TimeSpan span, CancellationToken token) => Task.CompletedTask;

    [Fact]
    public async Task ACancelDuringTheDebounceLeavesTheQueueAndWritesNothing()
    {
        var queue = new IndexUsageFileQueue();
        queue.Add([Payload("netflix", 9, 3)]);
        var published = 0;
        var flusher = new IndexUsageFileFlusher(queue, (_, _) => { published++; return Task.CompletedTask; }, (_, _) => { });
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await flusher.FlushAfterAsync(TimeSpan.FromSeconds(30), Task.Delay, cancelled.Token);

        Assert.Equal(0, published);
        Assert.True(queue.HasWork);
    }

    [Fact]
    public async Task ATimeoutDuringTheWriteIsPutBackAndWarnedNotSwallowed()
    {
        var queue = new IndexUsageFileQueue();
        queue.Add([Payload("netflix", 9, 3)]);
        var warnings = new List<Exception>();
        var flusher = new IndexUsageFileFlusher(queue, (_, _) => throw new TaskCanceledException("The Drive request timed out."), (_, exception) => warnings.Add(exception));

        await flusher.FlushAfterAsync(TimeSpan.Zero, NoWait, CancellationToken.None);

        Assert.Single(warnings);
        Assert.IsType<TaskCanceledException>(warnings[0]);
        Assert.Equal([("netflix", 3)], queue.Drain().Select(p => (p.Service.Value, p.HandOffs)));
    }

    [Fact]
    public async Task AFailedWriteKeepsWhatArrivedSinceAsNewer()
    {
        var queue = new IndexUsageFileQueue();
        queue.Add([Payload("netflix", 9, 3)]);
        var flusher = new IndexUsageFileFlusher(
            queue,
            (_, _) =>
            {
                queue.Add([Payload("netflix", 9, 4)]);
                throw new IOException("Drive refused.");
            },
            (_, _) => { });

        await flusher.FlushAfterAsync(TimeSpan.Zero, NoWait, CancellationToken.None);

        Assert.Equal([("netflix", 4)], queue.Drain().Select(p => (p.Service.Value, p.HandOffs)));
    }

    [Fact]
    public async Task TwoFlushesNeverWriteAtOnceAndTheSecondSendsWhatArrivedDuringTheFirst()
    {
        var queue = new IndexUsageFileQueue();
        queue.Add([Payload("netflix", 9, 3)]);
        var running = 0;
        var overlapped = false;
        var written = new List<int>();
        var release = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        var flusher = new IndexUsageFileFlusher(
            queue,
            async (payloads, _) =>
            {
                overlapped |= Interlocked.Increment(ref running) > 1;
                written.AddRange(payloads.Select(p => p.HandOffs));
                entered.TrySetResult();
                await release.Task;
                Interlocked.Decrement(ref running);
            },
            (_, _) => { });

        var first = flusher.FlushAfterAsync(TimeSpan.Zero, NoWait, CancellationToken.None);
        await entered.Task;
        queue.Add([Payload("netflix", 9, 4)]);
        var second = flusher.FlushAfterAsync(TimeSpan.Zero, NoWait, CancellationToken.None);
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.False(overlapped);
        Assert.Equal([3, 4], written);
        Assert.False(queue.HasWork);
    }

    [Fact]
    public async Task NothingQueuedWritesNothing()
    {
        var published = 0;
        var flusher = new IndexUsageFileFlusher(new IndexUsageFileQueue(), (_, _) => { published++; return Task.CompletedTask; }, (_, _) => { });

        await flusher.FlushAfterAsync(TimeSpan.Zero, NoWait, CancellationToken.None);

        Assert.Equal(0, published);
    }
}
