namespace Storyboarder.Domain.Pipeline;

// Raised by external clients when the provider reports a rate-limit / quota condition
// (e.g. HTTP 429, Anthropic rate_limit_error/overloaded_error, Gemini RESOURCE_EXHAUSTED).
// The worker translates this into a scheduled retry rather than a hard failure.
public sealed class RateLimitException : Exception
{
    public TimeSpan? RetryAfter { get; }

    public RateLimitException(string message, TimeSpan? retryAfter = null, Exception? innerException = null)
        : base(message, innerException)
    {
        RetryAfter = retryAfter;
    }
}
