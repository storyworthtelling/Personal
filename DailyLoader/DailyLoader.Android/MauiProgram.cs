using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using DailyLoader.Domain.Interfaces;
using DailyLoader.Infrastructure.Calendar;
using DailyLoader.Infrastructure.Delivery;
using DailyLoader.Infrastructure.Google;
using DailyLoader.Infrastructure.Handwriting;
using DailyLoader.Infrastructure.Nyt;
using DailyLoader.Infrastructure.Pdf;
using DailyLoader.Infrastructure.Remarkable;
using DailyLoader.Android.Google;
using PdfSharp.Fonts;

namespace DailyLoader.Android;

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
        GlobalFontSettings.FontResolver = AndroidFontResolver.Instance;

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("SourceSerif4-Regular.ttf", "Serif");
                fonts.AddFont("SourceSerif4-Semibold.ttf", "SerifSemibold");
                fonts.AddFont("SourceSerif4-Italic.ttf", "SerifItalic");
                fonts.AddFont("SourceSans3-Regular.ttf", "Sans");
                fonts.AddFont("SourceSans3-Semibold.ttf", "SansSemibold");
                fonts.AddFont("CascadiaCode-Regular.ttf", "Mono");
            });

        builder.Services.AddSingleton<IOptions<NytOptions>>(
            _ => new LiveOptions<NytOptions>(() => new NytOptions { SessionCookie = CredentialStore.NytSessionCookie }));
        builder.Services.AddSingleton<IOptions<NytApiOptions>>(
            _ => new LiveOptions<NytApiOptions>(() => new NytApiOptions { ApiKey = CredentialStore.NytApiKey }));
        builder.Services.AddSingleton<IOptions<RemarkableOptions>>(
            _ => new LiveOptions<RemarkableOptions>(() => new RemarkableOptions { DeviceToken = CredentialStore.RemarkableDeviceToken }));

        // Credentials come from the device account via Play Services, so GoogleOptions only
        // carries the non-secret settings (time zone, target calendar, kid calendar names).
        builder.Services.AddSingleton<IOptions<GoogleOptions>>(
            _ => new LiveOptions<GoogleOptions>(() => new GoogleOptions()));
        builder.Services.AddSingleton<IOptions<AnthropicOptions>>(
            _ => new LiveOptions<AnthropicOptions>(() => new AnthropicOptions { ApiKey = CredentialStore.AnthropicApiKey }));
        builder.Services.AddSingleton<IOptions<CalendarMappingOptions>>(
            _ => new LiveOptions<CalendarMappingOptions>(() => ParseMapping(CredentialStore.CalendarMappingJson)));
        builder.Services.AddSingleton<IOptions<WorkScheduleOptions>>(
            _ => new LiveOptions<WorkScheduleOptions>(() => new WorkScheduleOptions()));

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

        // Calendar + checklist feature. Tokens come from the device account via AccountManager;
        // Anthropic HTTP and the Google SDK transport go through AndroidMessageHandler for SELinux.
        builder.Services.AddSingleton<IGoogleAccessTokenProvider, DeviceAccountTokenProvider>();
        builder.Services.AddSingleton<IGoogleClientFactory>(sp =>
            new GoogleClientFactory(
                sp.GetRequiredService<IGoogleAccessTokenProvider>(),
                new AndroidGoogleHttpClientFactory()));
        builder.Services.AddTransient<IGoogleCalendarClient, GoogleCalendarClient>();
        builder.Services.AddTransient<IGoogleTasksClient, GoogleTasksClient>();
        builder.Services.AddTransient<CalendarColumnMapper>();
        builder.Services.AddTransient<WorkScheduleAugmenter>();
        builder.Services.AddTransient<IOutlookSelector, OutlookSelector>();
        builder.Services.AddTransient<ICalendarPdfComposer, CalendarPdfComposer>();
        builder.Services.AddTransient<IChecklistPdfComposer, ChecklistPdfComposer>();
        builder.Services.AddHttpClient<IHandwritingExtractor, ClaudeVisionHandwritingExtractor>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddTransient<DailyDeliveryService>();

        builder.Services.AddTransient<Pages.MainPage>();
        builder.Services.AddTransient<Pages.SetupPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        builder.Logging.AddProvider(DeliveryFileLogger.Instance);

        return builder.Build();
    }

    private static readonly JsonSerializerOptions MappingJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // Parses the saved calendar-mapping JSON into options, falling back to defaults when empty
    // or malformed so a bad paste never breaks delivery.
    private static CalendarMappingOptions ParseMapping(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new CalendarMappingOptions();
        try
        {
            return JsonSerializer.Deserialize<CalendarMappingOptions>(json, MappingJsonOptions)
                ?? new CalendarMappingOptions();
        }
        catch (JsonException)
        {
            return new CalendarMappingOptions();
        }
    }
}
