using System.Net;
using Google;

namespace Constitution.Infrastructure.Calendar;

/// <summary>
/// The failures Google Calendar can be expected to give, as plain sentences. The adapters turn these into a calendar that "could not be read"
/// (fail closed, never an empty calendar) or a write that "failed" (retried at the next run), and the sentence goes to a notice's reason and a
/// trail, so it holds no number, address or calendar content: only the class of trouble. <b>Only these are described.</b> A bug, or a
/// cancellation the caller asked for, is not a failure of the calendar and propagates with its stack.
/// </summary>
public static class CalendarErrors
{
    /// <summary>The sentence for a status Google answered with.</summary>
    public static string Reason(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Google did not accept the sign-in for a calendar.",
        HttpStatusCode.Forbidden => "Google refused access to a calendar; it may not be shared.",
        HttpStatusCode.NotFound => "A calendar was not found; it may not be shared.",
        HttpStatusCode.Gone => "A calendar or event is gone.",
        HttpStatusCode.TooManyRequests => "Google asked Constitution to slow down.",
        >= HttpStatusCode.InternalServerError => "Google could not answer.",
        _ => "Google answered with an error.",
    };

    /// <summary>
    /// Whether the failure is one the calendar can be expected to give, and if so its sentence. False for anything else, including a
    /// cancellation <paramref name="cancellation"/> asked for: the caller rethrows it.
    /// </summary>
    public static bool TryDescribe(Exception failure, CancellationToken cancellation, out string reason)
    {
        ArgumentNullException.ThrowIfNull(failure);
        switch (failure)
        {
            case CalendarAccessException access:
                reason = access.Message;
                return true;
            case GoogleApiException google:
                reason = Reason(google.HttpStatusCode);
                return true;
            case HttpRequestException:
                reason = "A calendar could not be reached.";
                return true;
            case TaskCanceledException when !cancellation.IsCancellationRequested:
                // Not cancelled by the caller: the client's own timeout fired.
                reason = "A calendar did not answer in time.";
                return true;
            default:
                reason = string.Empty;
                return false;
        }
    }
}
