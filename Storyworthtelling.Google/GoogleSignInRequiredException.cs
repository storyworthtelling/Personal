namespace Storyworthtelling.Google;

/// <summary>
/// Google turned this phone's sign-in away: consent was withdrawn, the account's password changed, or the account was
/// removed (audit 11 §4). It is an <see cref="InvalidOperationException"/>, as the plain "sign in again" it replaces was,
/// so every existing catch still holds; a screen that wants to act on it — the "Sign in again" row — catches this type.
/// <see cref="Recovery"/> is the platform's own way back (Android's consent intent) when it offered one.
/// </summary>
public sealed class GoogleSignInRequiredException(string message, object? recovery = null, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    public object? Recovery { get; } = recovery;
}
