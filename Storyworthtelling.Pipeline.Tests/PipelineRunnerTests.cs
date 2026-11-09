
namespace Storyworthtelling.Pipeline.Tests;

// The drain loop the Android WorkManager worker runs each cycle. It must finish promptly with a result
// the worker can map to WorkManager's Success/Retry, never sleeping through a long backoff.
public class PipelineRunnerTests
{
    private static readonly Guid SubjectId = Guid.NewGuid();

    private sealed class ScriptedStep : IPipelineStep
    {
        private readonly Queue<ProcessResult> results;
        public int Calls { get; private set; }

        public ScriptedStep(params ProcessResult[] results) => this.results = new Queue<ProcessResult>(results);

        public Task<ProcessResult> ProcessNextAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(results.Count > 0 ? results.Dequeue() : ProcessResult.Idle);
        }
    }

    private sealed class ThrowingStep : IPipelineStep
    {
        private readonly Exception toThrow;
        private bool thrown;
        public int Calls { get; private set; }

        public ThrowingStep(Exception toThrow) => this.toThrow = toThrow;

        public Task<ProcessResult> ProcessNextAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (!thrown)
            {
                thrown = true;
                throw toThrow;
            }
            return Task.FromResult(ProcessResult.Idle);
        }
    }

    private sealed class FakeClock(DateTimeOffset start)
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public void Advance(TimeSpan by) => UtcNow += by;
    }

    // Stands in for EF Core's DbUpdateConcurrencyException, which each app names as AbandonsCycle.
    private sealed class StaleWriteException(string message) : Exception(message);

    private sealed class RecordingDelay
    {
        private readonly FakeClock clock;
        public List<TimeSpan> Waited { get; } = [];

        public RecordingDelay(FakeClock clock) => this.clock = clock;

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            Waited.Add(delay);
            clock.Advance(delay);
            return Task.CompletedTask;
        }
    }

    private static RunBudget Budget(
        FakeClock clock, RecordingDelay delay, int maxIterations = 100,
        TimeSpan? maxDuration = null, TimeSpan? maxInCycleWait = null) =>
        new(() => clock.UtcNow, delay.Delay, maxIterations,
            maxDuration ?? TimeSpan.FromMinutes(10),
            maxInCycleWait ?? TimeSpan.FromSeconds(60),
            AbandonsCycle: e => e is StaleWriteException);

    [Fact]
    public async Task Idle_immediately_returns_Completed()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var delay = new RecordingDelay(clock);
        var step = new ScriptedStep(ProcessResult.Idle);

        var result = await new PipelineRunner(step).RunUntilExhaustedAsync(Budget(clock, delay), CancellationToken.None);

        Assert.Equal(RunResult.Completed, result);
        Assert.Equal(1, step.Calls);
        Assert.Empty(delay.Waited);
    }

    [Fact]
    public async Task Drains_consecutive_DidWork_until_Idle()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var delay = new RecordingDelay(clock);
        var step = new ScriptedStep(
            ProcessResult.DidWork(SubjectId), ProcessResult.DidWork(SubjectId), ProcessResult.Idle);

        var result = await new PipelineRunner(step).RunUntilExhaustedAsync(Budget(clock, delay), CancellationToken.None);

        Assert.Equal(RunResult.Completed, result);
        Assert.Equal(3, step.Calls);
        Assert.Empty(delay.Waited);
    }

    [Fact]
    public async Task Long_Waiting_returns_RateLimited_without_sleeping()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var delay = new RecordingDelay(clock);
        var step = new ScriptedStep(ProcessResult.Waiting(TimeSpan.FromMinutes(5), SubjectId));

        var result = await new PipelineRunner(step).RunUntilExhaustedAsync(
            Budget(clock, delay, maxInCycleWait: TimeSpan.FromSeconds(60)), CancellationToken.None);

        Assert.Equal(RunResult.RateLimited, result);
        Assert.Equal(1, step.Calls);
        Assert.Empty(delay.Waited);
    }

    [Fact]
    public async Task Short_Waiting_sleeps_then_continues()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var delay = new RecordingDelay(clock);
        var wait = TimeSpan.FromSeconds(5);
        var step = new ScriptedStep(ProcessResult.Waiting(wait, SubjectId), ProcessResult.Idle);

        var result = await new PipelineRunner(step).RunUntilExhaustedAsync(
            Budget(clock, delay, maxInCycleWait: TimeSpan.FromSeconds(60)), CancellationToken.None);

        Assert.Equal(RunResult.Completed, result);
        Assert.Equal(2, step.Calls);
        Assert.Equal([wait], delay.Waited);
    }

    [Fact]
    public async Task MaxIterations_caps_a_busy_cycle_with_MoreWorkPending()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var delay = new RecordingDelay(clock);
        var step = new ScriptedStep([.. Enumerable.Repeat(ProcessResult.DidWork(SubjectId), 10)]);

        var result = await new PipelineRunner(step).RunUntilExhaustedAsync(
            Budget(clock, delay, maxIterations: 3), CancellationToken.None);

        Assert.Equal(RunResult.MoreWorkPending, result);
        Assert.Equal(3, step.Calls);
    }

    [Fact]
    public async Task ConcurrencyConflict_endsCycleAsMoreWorkPending_withoutCrashing()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var delay = new RecordingDelay(clock);
        var step = new ThrowingStep(new StaleWriteException("row changed"));

        var result = await new PipelineRunner(step).RunUntilExhaustedAsync(Budget(clock, delay), CancellationToken.None);

        Assert.Equal(RunResult.MoreWorkPending, result);
        Assert.Equal(1, step.Calls);
    }

    [Fact]
    public async Task MaxDuration_caps_a_cycle_of_short_waits_with_MoreWorkPending()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var delay = new RecordingDelay(clock);
        var step = new ScriptedStep([.. Enumerable.Repeat(ProcessResult.Waiting(TimeSpan.FromSeconds(20), SubjectId), 10)]);

        var result = await new PipelineRunner(step).RunUntilExhaustedAsync(
            Budget(clock, delay, maxIterations: 100,
                maxDuration: TimeSpan.FromSeconds(60), maxInCycleWait: TimeSpan.FromSeconds(60)),
            CancellationToken.None);

        Assert.Equal(RunResult.MoreWorkPending, result);
        // Three 20s waits advance the clock to 60s; the next loop-top check trips MaxDuration.
        Assert.Equal(3, step.Calls);
    }

    // Only the exception the app named abandons the cycle; anything else is the step's failure and reaches the worker.
    [Fact]
    public async Task AnyOtherException_propagates()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var delay = new RecordingDelay(clock);
        var step = new ThrowingStep(new InvalidOperationException("bug"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PipelineRunner(step).RunUntilExhaustedAsync(Budget(clock, delay), CancellationToken.None));
    }
}
