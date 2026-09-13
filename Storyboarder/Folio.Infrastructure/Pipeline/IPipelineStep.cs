namespace Folio.Infrastructure.Pipeline;

// One advance of the pipeline. Implemented by ReviewProcessor; abstracted so the drain loop
// (PipelineRunner) is unit-testable without a real database or external services.
public interface IPipelineStep
{
    Task<ProcessResult> ProcessNextAsync(CancellationToken cancellationToken);
}
