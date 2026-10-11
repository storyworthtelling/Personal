using Pulp.Domain.Calendar;

namespace Pulp.Infrastructure.Calendar;

/// <summary>What the nightly worker should do about a failed night.</summary>
public enum CalendarAdvice
{
    /// <summary>Try again, and say nothing: the night simply had no account to use yet.</summary>
    QuietRetry,

    /// <summary>Try again, and write what happened to Diagnostics.</summary>
    Retry,

    /// <summary>Retrying tonight will not mend it; write it to Diagnostics and wait for the next scheduled night.</summary>
    GiveUp,
}

/// <summary>The decisions about a calendar failure, apart from WorkManager so they can be tested.</summary>
public static class CalendarFailures
{
    /// <summary>The sentence <c>GoogleAccount</c> throws when no account has been chosen yet (the only quiet case).</summary>
    public const string NotSignedIn = "Sign in with Google first.";

    /// <summary>The failure itself, or each failure inside an aggregate (nested ones flattened), in order.</summary>
    public static IReadOnlyList<Exception> Parts(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToList() : [exception];
    }

    /// <summary>
    /// Retry when any part may mend by trying again, quietly when the only cause is not being signed in,
    /// and give up when none can: a programming error does not mend overnight.
    /// </summary>
    public static CalendarAdvice Advise(Exception exception)
    {
        var advice = Parts(exception).Select(AdviseOne).ToList();
        return advice.Contains(CalendarAdvice.Retry) ? CalendarAdvice.Retry
            : advice.Contains(CalendarAdvice.QuietRetry) ? CalendarAdvice.QuietRetry
            : CalendarAdvice.GiveUp;
    }

    /// <summary>Whether Google withdrew the consent: a 401, or a 403 that says the permission itself is missing. A rate limit never does.</summary>
    public static bool ConsentWithdrawn(Exception exception) =>
        Parts(exception).OfType<CalendarException>().Any(failure =>
            failure.StatusCode == 401
            || (failure.StatusCode == 403 && failure.Reason is "insufficientPermissions" or "forbidden"));

    private static CalendarAdvice AdviseOne(Exception exception) => exception switch
    {
        CalendarException { Failure: CalendarFailure.Unavailable or CalendarFailure.Throttled } => CalendarAdvice.Retry,
        CalendarException => CalendarAdvice.GiveUp,
        InvalidOperationException { Message: NotSignedIn } => CalendarAdvice.QuietRetry,
        OperationCanceledException => CalendarAdvice.QuietRetry,
        IOException or TimeoutException or HttpRequestException => CalendarAdvice.Retry,
        _ => CalendarAdvice.GiveUp,
    };
}
