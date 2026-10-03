namespace Index.Infrastructure.Pipeline;

// One advance of the pipeline. Implemented by BalanceRefreshProcessor; abstracted so the drain loop
// (PipelineRunner) is unit-testable without a real database or Plaid access.
public interface IPipelineStep
{
    Task<ProcessResult> ProcessNextAsync(CancellationToken cancellationToken);
}
