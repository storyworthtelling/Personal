using global::Google.Apis.Calendar.v3;
using global::Google.Apis.Http;
using global::Google.Apis.Services;
using global::Google.Apis.Tasks.v1;
using GoogleHttpClientFactory = global::Google.Apis.Http.IHttpClientFactory;

namespace Foreword.Infrastructure.Google;

// Builds Google API services that authenticate with the access-token provider and (optionally)
// route transport through a host-supplied handler. The Android host passes a factory backed by
// AndroidMessageHandler so calls clear SELinux; the Worker leaves it null to use the default.
public class GoogleClientFactory : IGoogleClientFactory
{
    // Google's ApplicationName is a User-Agent label keyed to nothing, so it follows the
    // rename — the same call Score made when "FocusPhone.Audio" became "Score".
    private const string ApplicationName = "Foreword";

    private readonly IGoogleAccessTokenProvider tokenProvider;
    private readonly GoogleHttpClientFactory? transportFactory;

    public GoogleClientFactory(
        IGoogleAccessTokenProvider tokenProvider,
        GoogleHttpClientFactory? transportFactory = null)
    {
        this.tokenProvider = tokenProvider;
        this.transportFactory = transportFactory;
    }

    public CalendarService CreateCalendarService() => new(CreateInitializer());

    public TasksService CreateTasksService() => new(CreateInitializer());

    private BaseClientService.Initializer CreateInitializer()
    {
        var initializer = new BaseClientService.Initializer
        {
            HttpClientInitializer = new TokenInitializer(tokenProvider),
            ApplicationName = ApplicationName
        };
        if (transportFactory is not null)
            initializer.HttpClientFactory = transportFactory;
        return initializer;
    }

    // Injects a fresh bearer token on every request via the access-token provider.
    private sealed class TokenInitializer(IGoogleAccessTokenProvider provider)
        : IConfigurableHttpClientInitializer, IHttpExecuteInterceptor
    {
        public void Initialize(ConfigurableHttpClient httpClient) =>
            httpClient.MessageHandler.AddExecuteInterceptor(this);

        public async Task InterceptAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await provider.GetAccessToken());
    }
}
