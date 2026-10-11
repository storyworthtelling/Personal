namespace Pulp.Domain.Core;

/// <summary>
/// Whether a caught cancellation is the caller leaving. A Drive or HTTP timeout surfaces as a
/// <see cref="TaskCanceledException"/>, which is an <see cref="OperationCanceledException"/>, so
/// filtering on the exception type alone takes a timeout for a user cancel and rethrows it from places
/// nobody is waiting. A cancellation is the caller's only when it is one <i>and</i> the token the caller
/// passed was actually cancelled; anything else is a failure to report and retry.
/// </summary>
public static class CancellationVerdict
{
    public static bool IsUserCancel(Exception exception, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is OperationCanceledException && token.IsCancellationRequested;
    }
}
