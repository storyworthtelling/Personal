using System.Text.Json;

namespace Constitution.Domain.Settings;

/// <summary>Which act a connection failure interrupted.</summary>
public enum ConnectionAct
{
    /// <summary>Pairing the reMarkable with a one-time code.</summary>
    PairRemarkable,

    /// <summary>Choosing a Google account and consenting to its scopes.</summary>
    SignIn,
}

/// <summary>
/// What the Settings screen says when pairing or signing in stops, and what the trail records about it.
/// Pure, so each kind of failure has its sentence under test and the screen only paints it.
/// </summary>
public static class ConnectionFailure
{
    /// <summary>
    /// The sentence a row is left with. Never empty, never a stack trace, and never a raw message:
    /// messages come from the network stack and the Google client and are not ours to vouch for as free
    /// of a code or a token. The one exception is the shared sign-in's own explanations (an
    /// <see cref="InvalidOperationException"/> such as "Account selection was cancelled."), which are
    /// written for the person to read.
    /// </summary>
    /// <param name="act">What was interrupted.</param>
    /// <param name="failure">What stopped it.</param>
    /// <param name="callerCancelled">Whether the person or the app asked for it to stop; a cancellation nobody asked for is a timeout.</param>
    public static string Sentence(ConnectionAct act, Exception failure, bool callerCancelled)
    {
        ArgumentNullException.ThrowIfNull(failure);

        switch (failure)
        {
            case OperationCanceledException when callerCancelled:
                return $"{Gerund(act)} was stopped.";
            case OperationCanceledException:
            case TimeoutException:
                return $"{Gerund(act)} timed out. Try again.";
            case HttpRequestException { StatusCode: { } status } when act == ConnectionAct.PairRemarkable:
                return $"The tablet did not accept that code ({status}).";
            case HttpRequestException:
                return $"{Gerund(act)} stopped: the network could not be reached. Check the connection and try again.";
            case JsonException:
            case FormatException:
                return $"{Gerund(act)} stopped: the reply was something Constitution could not read. Try again.";
            case IOException:
                return $"{Gerund(act)} stopped: the connection dropped. Try again.";
            case InvalidOperationException when act == ConnectionAct.SignIn:
                return $"Sign-in did not complete. {failure.Message}";
            default:
                return $"{Gerund(act)} stopped unexpectedly ({failure.GetType().Name}).";
        }
    }

    /// <summary>The trail phrase: the act and the exception's type name, and never its message.</summary>
    public static string LogPhrase(ConnectionAct act, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var what = act switch
        {
            ConnectionAct.PairRemarkable => "remarkable pairing",
            ConnectionAct.SignIn => "sign-in",
            _ => throw new ArgumentOutOfRangeException(nameof(act), act, "Not an act this wording knows."),
        };
        return $"{what} failed: {failure.GetType().Name}";
    }

    private static string Gerund(ConnectionAct act) => act switch
    {
        ConnectionAct.PairRemarkable => "Pairing",
        ConnectionAct.SignIn => "Sign-in",
        _ => throw new ArgumentOutOfRangeException(nameof(act), act, "Not an act this wording knows."),
    };
}
