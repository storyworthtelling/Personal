using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using NytRemarkable.Domain.Interfaces;
using NytRemarkable.Infrastructure.Delivery;
using NytRemarkable.Infrastructure.Nyt;
using NytRemarkable.Infrastructure.Pdf;
using NytRemarkable.Infrastructure.Remarkable;

namespace NytRemarkable.Android;

public static class MauiProgram
{
    // IOptions<T> is a singleton whose Configure delegate runs once and caches.
    // This wrapper reads from CredentialStore on every access so credential
    // updates (or late saves) are always picked up without restarting the app.
    private sealed class LiveOptions<T>(Func<T> factory) : IOptions<T> where T : class
    {
        public T Value => factory();
    }

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<IOptions<NytOptions>>(
            _ => new LiveOptions<NytOptions>(() => new NytOptions { SessionCookie = CredentialStore.NytSessionCookie }));
        builder.Services.AddSingleton<IOptions<NytApiOptions>>(
            _ => new LiveOptions<NytApiOptions>(() => new NytApiOptions { ApiKey = CredentialStore.NytApiKey }));
        builder.Services.AddSingleton<IOptions<RemarkableOptions>>(
            _ => new LiveOptions<RemarkableOptions>(() => new RemarkableOptions { DeviceToken = CredentialStore.RemarkableDeviceToken }));

        // AndroidMessageHandler routes through Android's HttpsURLConnection (Java layer),
        // which is required for SELinux to permit network access. SocketsHttpHandler
        // (the .NET default) opens raw sockets that SELinux blocks in Android app processes.
        static Xamarin.Android.Net.AndroidMessageHandler AndroidHandler() => new();

        builder.Services.AddHttpClient<INytCrosswordClient, NytCrosswordClient>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddHttpClient<INytTopStoriesClient, NytTopStoriesClient>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddSingleton<IArticlePageFetcher, WebViewPageFetcher>();
        builder.Services.AddHttpClient<INytArticleClient, NytArticleClient>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddHttpClient<IRemarkableAuthProvider, RemarkableAuthService>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddHttpClient<IRemarkableClient, RemarkableStorageClient>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);

        builder.Services.AddTransient<IPdfComposer, PdfComposer>();
        builder.Services.AddTransient<CrosswordDeliveryService>();

        builder.Services.AddTransient<Pages.MainPage>();
        builder.Services.AddTransient<Pages.SetupPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
