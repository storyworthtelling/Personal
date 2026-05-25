using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.Configure<NytOptions>(opts =>
            opts.SessionCookie = CredentialStore.NytSessionCookie);
        builder.Services.Configure<NytApiOptions>(opts =>
            opts.ApiKey = CredentialStore.NytApiKey);
        builder.Services.Configure<RemarkableOptions>(opts =>
            opts.DeviceToken = CredentialStore.RemarkableDeviceToken);

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
