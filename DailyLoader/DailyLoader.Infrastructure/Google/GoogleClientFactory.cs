using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Auth.OAuth2.Flows;
using global::Google.Apis.Auth.OAuth2.Responses;
using global::Google.Apis.Calendar.v3;
using global::Google.Apis.Services;
using global::Google.Apis.Tasks.v1;
using Microsoft.Extensions.Options;

namespace DailyLoader.Infrastructure.Google;

// Turns the stored OAuth refresh token into auto-refreshing Google API services.
public class GoogleClientFactory : IGoogleClientFactory
{
    private const string ApplicationName = "DailyLoader";

    private readonly GoogleOptions options;

    public GoogleClientFactory(IOptions<GoogleOptions> options)
    {
        this.options = options.Value;
    }

    public CalendarService CreateCalendarService() =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = CreateCredential([CalendarService.Scope.Calendar]),
            ApplicationName = ApplicationName
        });

    public TasksService CreateTasksService() =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = CreateCredential([TasksService.Scope.Tasks]),
            ApplicationName = ApplicationName
        });

    private UserCredential CreateCredential(IEnumerable<string> scopes)
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = options.ClientId, ClientSecret = options.ClientSecret },
            Scopes = scopes
        });

        // The access token is left empty so the flow fetches a fresh one from the refresh token.
        return new UserCredential(flow, "user", new TokenResponse { RefreshToken = options.RefreshToken });
    }
}
