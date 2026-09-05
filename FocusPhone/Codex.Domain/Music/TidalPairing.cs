namespace Codex.Domain.Music;

/// <summary>What the token endpoint's <c>error</c> field means while a device code is
/// still waiting on the owner, per RFC 8628 (the device authorization grant), which
/// TIDAL's <c>auth.tidal.com/v1/oauth2/token</c> follows.</summary>
public enum TidalPairingSignal
{
    /// <summary>The owner has not approved the code yet. Keep polling.</summary>
    Pending,

    /// <summary>Polling too fast. Keep polling, but widen the interval first.</summary>
    SlowDown,

    /// <summary>The owner declined, or the app was revoked. Stop; nothing to retry.</summary>
    Denied,

    /// <summary>The code's window closed before it was approved. Stop; the pairing
    /// screen needs a fresh code, not another poll.</summary>
    Expired,

    /// <summary>A code the pairing loop was not written against. Stop rather than
    /// loop on a shape that never resolves.</summary>
    Unknown,
}

/// <summary>
/// Reads the token endpoint's polling error codes into <see cref="TidalPairingSignal"/>.
///
/// Pulled out of the client so the polling loop's stop/continue decision is testable
/// without a fake network round trip for every one of RFC 8628's five error codes.
/// </summary>
public static class TidalPairing
{
    public static TidalPairingSignal Interpret(string? errorCode) => errorCode switch
    {
        "authorization_pending" => TidalPairingSignal.Pending,
        "slow_down" => TidalPairingSignal.SlowDown,
        "access_denied" => TidalPairingSignal.Denied,
        "expired_token" => TidalPairingSignal.Expired,
        _ => TidalPairingSignal.Unknown,
    };
}
