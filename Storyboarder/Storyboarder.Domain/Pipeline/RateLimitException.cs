namespace Storyboarder.Domain.Pipeline;

// How a provider rate-limit / quota condition should be handled by the retry machinery.
public enum RateLimitKind
{
    // A short, self-clearing throttle: a per-minute request-rate cap or a transient provider overload.
    // It clears on its own within seconds/minutes, so the worker retries on the normal backoff and can
    // report an expected resume time.
    Transient,

    // The account's paid quota is exhausted (e.g. Gemini "prepayment credits are depleted", or billing
    // not enabled on the API project). This clears only when the user tops up billing, so retrying on the
    // short transient cadence just burns calls against a wall. The worker waits a long, flat interval
    // between checks and surfaces an actionable message rather than a false "resumes shortly".
    QuotaExhausted
}

// Raised by external clients when the provider reports a rate-limit / quota condition
// (e.g. HTTP 429, Anthropic rate_limit_error/overloaded_error, Gemini RESOURCE_EXHAUSTED).
// The worker translates this into a scheduled retry rather than a hard failure; Kind tells it whether
// the wait is a brief self-clearing throttle or a billing/quota wall that needs the user to act.
public sealed class RateLimitException : Exception
{
    public TimeSpan? RetryAfter { get; }

    public RateLimitKind Kind { get; }

    public RateLimitException(
        string message, TimeSpan? retryAfter = null,
        RateLimitKind kind = RateLimitKind.Transient, Exception? innerException = null)
        : base(message, innerException)
    {
        RetryAfter = retryAfter;
        Kind = kind;
    }
}
