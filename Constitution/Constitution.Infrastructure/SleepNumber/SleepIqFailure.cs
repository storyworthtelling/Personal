namespace Constitution.Infrastructure.SleepNumber;

/// <summary>
/// Why a SleepIQ call produced nothing. Typed, so no exception reaches the UI. Only
/// <see cref="SignInRejected"/> says anything about the credential; everything else leaves it alone.
/// </summary>
public enum SleepIqFailure
{
    /// <summary>It worked.</summary>
    None,

    /// <summary>No login is set up in this install, so no call was made.</summary>
    NoLogin,

    /// <summary>The service refused the login (401/403). The only failure that can flag the credential.</summary>
    SignInRejected,

    /// <summary>The service could not be reached, timed out, or was busy or broken (5xx, 429, 408).</summary>
    Offline,

    /// <summary>The service answered, but not in the shape this client assumes (the unofficial API moved).</summary>
    UnexpectedShape,

    /// <summary>The bed has nobody on the requested side, or no side has been chosen.</summary>
    NoSuchSide,
}

/// <summary>A value, or the reason there is not one. Its text never includes the value.</summary>
public sealed class SleepIqResult<T>
{
    private SleepIqResult(SleepIqFailure failure, T? value)
    {
        Failure = failure;
        Value = value;
    }

    public SleepIqFailure Failure { get; }

    /// <summary>Meaningful only when <see cref="IsOk"/>. For a night, null inside an Ok result means "no reading".</summary>
    public T? Value { get; }

    public bool IsOk => Failure == SleepIqFailure.None;

    public static SleepIqResult<T> Ok(T? value) => new(SleepIqFailure.None, value);

    public static SleepIqResult<T> Failed(SleepIqFailure failure) =>
        failure == SleepIqFailure.None
            ? throw new ArgumentException("A failed result needs a failure other than None.", nameof(failure))
            : new(failure, default);

    public override string ToString() => IsOk ? "ok" : Failure.ToString();
}
