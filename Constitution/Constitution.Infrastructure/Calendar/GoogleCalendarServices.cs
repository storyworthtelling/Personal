using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;

namespace Constitution.Infrastructure.Calendar;

/// <summary>
/// Builds the Calendar service the three Google adapters share, from the head's sign-in. Nothing here can fail loudly: no sign-in, or
/// a sign-in that gives no token, is a <see cref="CalendarAccessException"/> with a fixed sentence, which the adapters turn into an
/// unreadable calendar or a failed write with that reason. The token is used to build the service and is never kept, quoted or put in
/// a message, and the failure's own text (which can carry account detail) is not copied into the sentence.
/// </summary>
public static class GoogleCalendarServices
{
    public const string NotSignedIn = "Google is not signed in on this phone.";
    public const string SignInRefused = "Google would not give Constitution a calendar sign-in.";

    public static Func<CancellationToken, Task<CalendarService>> Create(Func<bool> isSignedIn, Func<CancellationToken, Task<string>> token, string applicationName)
    {
        ArgumentNullException.ThrowIfNull(isSignedIn);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        return async cancellation =>
        {
            if (!isSignedIn())
            {
                throw new CalendarAccessException(NotSignedIn);
            }

            string given;
            try
            {
                given = await token(cancellation);
            }
            catch (Exception failure) when (failure is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                throw new CalendarAccessException(SignInRefused, failure);
            }

            if (string.IsNullOrWhiteSpace(given))
            {
                throw new CalendarAccessException(SignInRefused);
            }

            return new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GoogleCredential.FromAccessToken(given),
                ApplicationName = applicationName,
            });
        };
    }
}
