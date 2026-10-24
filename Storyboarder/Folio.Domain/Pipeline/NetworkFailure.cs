using System.Net.Sockets;

namespace Folio.Domain.Pipeline;

// Tells "the phone has no connection" from "the call failed". Offline fails instantly, before anything
// is sent, naming the host it could not look up — so nothing was billed and nothing was learned about
// the call, and it must not use up the attempts that exist to stop a billed call being repeated.
// A connection that DROPPED part-way is the opposite case (the request was accepted, the reply lost)
// and is deliberately not matched here.
public static class NetworkFailure
{
    private static readonly SocketError[] Unreachable =
    [
        SocketError.HostNotFound, SocketError.TryAgain, SocketError.NoData,
        SocketError.NetworkUnreachable, SocketError.HostUnreachable, SocketError.NetworkDown
    ];

    // The Android runtime reports a failed lookup as a Java exception whose message is the only stable
    // thing about it; these are the strings it uses for "there is no network to resolve through".
    private static readonly string[] Phrases =
    [
        "Unable to resolve host", "No address associated with hostname", "Network is unreachable",
        "EAI_NODATA", "EAI_AGAIN", "EAI_NONAME"
    ];

    public static bool IsOffline(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException socket && Unreachable.Contains(socket.SocketErrorCode))
                return true;
            if (Phrases.Any(phrase => current.Message.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }
}
