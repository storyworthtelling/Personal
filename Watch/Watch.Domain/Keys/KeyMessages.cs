namespace Watch.Domain.Keys;

/// <summary>
/// What a handed-over token may be used for. One thing only: reading Drive, which Wave needs to stream the owner's music
/// over LTE with the phone at home (A17, W-6.1.2). There is no Gmail scope: Post fetches the mail and relays the lines
/// (<c>dash/mail</c>), so Dash needs no token at all (P14 B3).
/// </summary>
public enum TokenScope
{
    /// <summary>Drive, read-only: the phone asks Google for a token of this scope alone to hand over, never its own wider one.</summary>
    DriveReadOnly,
}

/// <summary>Why the phone has no token to hand over.</summary>
public enum TokenRefusal
{
    /// <summary>Google wants the owner to sign in again on the phone.</summary>
    SignIn,

    /// <summary>Anything else: no network, no account, a refusal the phone could not read.</summary>
    Failed,
}

/// <summary>
/// A message on the handed-over token's contract (plan §2.4, §2.5, P14 W-4.1.2), between a phone app and its own watch
/// app over the Data Layer. The watch app asks when it needs a token (<see cref="TokenAsk"/>); the phone answers with one
/// (<see cref="TokenHandoff"/>) or says why not (<see cref="TokenNone"/>). <see cref="KeyCodec"/> writes each with an
/// integer <c>v</c>; <see cref="KeyLogLines"/> never carries a token.
/// </summary>
public abstract record KeyMessage(TokenScope Scope)
{
    /// <summary>The Data Layer path every keys message travels on.</summary>
    public string Path => WatchIdentity.GoogleKeysPath;
}

/// <summary>The watch app asking for a token, because it has none or the one it holds is nearly done.</summary>
public sealed record TokenAsk(TokenScope Scope) : KeyMessage(Scope);

/// <summary>
/// The phone's answer: a token, and how much longer the watch may use it (<paramref name="Life"/>, whole seconds, at most
/// <see cref="KeyLimits.LongestHold"/>). A life rather than an instant, so the watch's clock and the phone's need not agree.
/// </summary>
public sealed record TokenHandoff(TokenScope Scope, AccessToken Token, TimeSpan Life) : KeyMessage(Scope);

/// <summary>The phone's answer when it has no token to hand over.</summary>
public sealed record TokenNone(TokenScope Scope, TokenRefusal Why) : KeyMessage(Scope);
