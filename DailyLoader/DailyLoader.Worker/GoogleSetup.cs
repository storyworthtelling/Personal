using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Calendar.v3;
using global::Google.Apis.Tasks.v1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using DailyLoader.Infrastructure.Google;

namespace DailyLoader.Worker;

// One-time OAuth consent on Matt's Google account. Run with --google-auth: it opens a browser,
// completes consent for calendar + tasks scopes, and prints the refresh token to store in
// user-secrets (Google:RefreshToken) or the Google__RefreshToken environment variable.
public static class GoogleSetup
{
    public static async Task Authorize(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<GoogleOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            Console.WriteLine("Set Google:ClientId and Google:ClientSecret before running --google-auth.");
            return;
        }

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets { ClientId = options.ClientId, ClientSecret = options.ClientSecret },
            [CalendarService.Scope.Calendar, TasksService.Scope.Tasks],
            "user",
            CancellationToken.None);

        Console.WriteLine();
        Console.WriteLine("Consent complete. Store this refresh token as Google:RefreshToken:");
        Console.WriteLine(credential.Token.RefreshToken);
    }
}
