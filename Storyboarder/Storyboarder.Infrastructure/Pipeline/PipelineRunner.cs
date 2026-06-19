using Storyboarder.Domain.Time;

namespace Storyboarder.Infrastructure.Pipeline;

public enum RunResult
{
    Completed,        // the processor went Idle — nothing left to do this cycle (WorkManager Success)
    MoreWorkPending,  // hit the iteration/time budget with work remaining   (WorkManager Retry)
    RateLimited       // a backoff/rate-limit wait exceeded the in-cycle cap  (WorkManager Retry)
}

// Bounds a single drain cycle so a WorkManager run finishes promptly. Delay is injected so tests
// can run deterministically without real waits; Clock drives the wall-clock budget.
public sealed record RunBudget(
    IClock Clock,
    Func<TimeSpan, CancellationToken, Task> Delay,
    int MaxIterations,
    TimeSpan MaxDuration,
    TimeSpan MaxInCycleWait);

// Repeatedly advances the pipeline until it idles, the budget is spent, or a wait is too long to
// sit through in this cycle. This is the old GraphicNovelWorker loop, minus the SignalR broadcast,
// extracted so it can be tested and reused from the Android worker shim.
public sealed class PipelineRunner
{
    private readonly IPipelineStep step;

    public PipelineRunner(IPipelineStep step) => this.step = step;

    public async Task<RunResult> RunUntilExhaustedAsync(RunBudget budget, CancellationToken cancellationToken)
    {
        var start = budget.Clock.UtcNow;
        int iterations = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (iterations >= budget.MaxIterations || budget.Clock.UtcNow - start >= budget.MaxDuration)
                return RunResult.MoreWorkPending;

            var result = await step.ProcessNextAsync(cancellationToken);
            iterations++;

            switch (result.Outcome)
            {
                case ProcessOutcome.Idle:
                    return RunResult.Completed;
                case ProcessOutcome.DidWork:
                    continue;
                case ProcessOutcome.Waiting:
                    if (result.Wait > budget.MaxInCycleWait)
                        return RunResult.RateLimited;
                    await budget.Delay(result.Wait, cancellationToken);
                    continue;
            }
        }

        return RunResult.MoreWorkPending;
    }
}
