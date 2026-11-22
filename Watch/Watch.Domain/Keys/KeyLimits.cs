namespace Watch.Domain.Keys;

/// <summary>The handed-over token's limits (P14 W-4.1.2). Chosen, not measured, except where said.</summary>
public static class KeyLimits
{
    /// <summary>The most a keys message may be: one token of the longest length and its few words fit well inside.</summary>
    public const int MaxBytes = 4096;

    /// <summary>
    /// The longest the watch holds a token: the phone's own reuse window for a device-account token
    /// (<c>CachedAccessToken.DefaultLifetime</c>, 45 minutes of Google's hour), held equal by
    /// <c>TheWatchHoldsNoVaultTests</c>. The wrist never uses a token longer than the phone itself would.
    /// </summary>
    public static readonly TimeSpan LongestHold = TimeSpan.FromMinutes(45);

    /// <summary>How long before its time the watch asks for the next token, so a stream does not stop on a lapsed one.</summary>
    public static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(5);

    /// <summary>The least time between two asks, whatever the answers were.</summary>
    public static readonly TimeSpan AskSpacing = TimeSpan.FromMinutes(1);
}
