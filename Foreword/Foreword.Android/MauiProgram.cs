using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Foreword.Domain.Interfaces;
using Foreword.Infrastructure.Brief;
using Foreword.Infrastructure.Calendar;
using Foreword.Infrastructure.Delivery;
using Foreword.Infrastructure.Google;
using Foreword.Infrastructure.Handwriting;
using Foreword.Infrastructure.Maps;
using Foreword.Infrastructure.Tasks;
using Foreword.Infrastructure.Nyt;
using Foreword.Infrastructure.Pdf;
using Foreword.Infrastructure.Prayer;
using Foreword.Android.Google;
using Storyworthtelling.Remarkable;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;
using VoiceGoogleCredential = global::Google.Apis.Auth.OAuth2.GoogleCredential;
using VoiceClientInitializer = global::Google.Apis.Services.BaseClientService.Initializer;
using VoiceDriveService = global::Google.Apis.Drive.v3.DriveService;

namespace Foreword.Android;

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
        // The documents now render with SkiaSharp (embedded fonts via Storyworthtelling.Paper); PdfSharp is
        // only used to merge the external crossword PDF and draw image-only pages, so no font resolver is
        // registered anymore.
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
            // All eight faces, under the aliases the role table names. This head used to list six
            // of them itself; Serif Black and Pinyon Script shipped inside the APK with no alias,
            // so Display could not draw in the face DESIGN.md §4 gives it and the owner's voice had
            // nowhere to live. An unregistered family is not an error in MAUI — the platform
            // substitutes the system face and says nothing.
            .ConfigureFonts(Storyworthtelling.Maui.PaperFonts.Register);

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
        builder.Services.AddSingleton<IOptions<MapsOptions>>(
            _ => new LiveOptions<MapsOptions>(() => new MapsOptions
            {
                ApiKey = CredentialStore.MapsApiKey,
                HomeAddress = CredentialStore.HomeAddress,
                HomeLatitude = CredentialStore.HomeLatitude,
                HomeLongitude = CredentialStore.HomeLongitude,
            }));
        builder.Services.AddSingleton<IOptions<AnthropicOptions>>(
            _ => new LiveOptions<AnthropicOptions>(() => new AnthropicOptions { ApiKey = CredentialStore.AnthropicApiKey }));
        builder.Services.AddSingleton<IOptions<CalendarMappingOptions>>(
            _ => new LiveOptions<CalendarMappingOptions>(() =>
            {
                var settings = new CalendarSettings();
                settings.Reload();
                return settings.Mapping;
            }));
        // Read from the settings the calendar screen writes. It was `new WorkScheduleOptions()` —
        // hardcoded, with no screen at all, so changing a shift meant changing a default and
        // building the app.
        builder.Services.AddSingleton<IOptions<WorkScheduleOptions>>(
            _ => new LiveOptions<WorkScheduleOptions>(() =>
            {
                var settings = new CalendarSettings();
                settings.Reload();
                return settings.Work;
            }));

        // AndroidMessageHandler routes through Android's HttpsURLConnection (Java layer),
        // which is required for SELinux to permit network access. SocketsHttpHandler
        // (the .NET default) opens raw sockets that SELinux blocks in Android app processes.
        static Xamarin.Android.Net.AndroidMessageHandler AndroidHandler() => new();

        // Credential health: the NYT/reMarkable/Anthropic clients report Ok/Failing here (injected as
        // ICredentialHealthSink), and the Settings page reads it back to hide a working key's field.
        // Registered before the typed clients so ActivatorUtilities injects it into their optional parameter.
        builder.Services.AddSingleton<CredentialHealthStore>();
        builder.Services.AddSingleton<ICredentialHealthSink>(sp => sp.GetRequiredService<CredentialHealthStore>());

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
        // The one-time device-code pairing, now via the shared library instead of an inline POST.
        builder.Services.AddHttpClient<IRemarkableRegistration, RemarkableRegistrationClient>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);

        builder.Services.AddTransient<IPdfComposer, PdfComposer>();

        // The brief's own sources. Both keyless (F14, F15) and both quiet on failure — a comic and
        // a factoid are the two most skippable things in the document.
        builder.Services.AddSingleton<IBriefPreferences, BriefSettings>();
        builder.Services.AddHttpClient<IComicClient, XkcdClient>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddHttpClient<IHistoryClient, WikipediaHistoryClient>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);

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
        builder.Services.AddTransient<IDayPdfComposer, DayPdfComposer>();
        builder.Services.AddSingleton<ITaskStore, TaskStore>();
        builder.Services.AddTransient<TaskEditor>();
        builder.Services.AddHttpClient<ITaskReadingClient, ClaudeTaskReader>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddHttpClient<IHandwritingExtractor, ClaudeVisionHandwritingExtractor>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        // Maps Platform: the drives on the calendar page and everything on the weather page. Both
        // degrade to nothing without a key, so the morning still goes out before one is entered.
        builder.Services.AddHttpClient<IWeatherClient, GoogleWeatherClient>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddHttpClient<IRouteClient, GoogleRoutesClient>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddTransient<DailyDeliveryService>();

        // Prayer (P9 Release H): the third document. Its own composer and its own two Claude calls,
        // so the journal is structurally unable to reach the other three — see PrayerText and
        // PrayerIsolationTests. The archive is registered AFTER the Drive seam below, so it is
        // resolved there rather than here.
        builder.Services.AddTransient<IPrayerPdfComposer, PrayerPages>();
        builder.Services.AddHttpClient<IJournalTranscriber, ClaudeJournalTranscriber>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddHttpClient<IPastoralComposer, ClaudePastoralComposer>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);

        // Owner's voice: greet on the delivery screen, reading the shared profile + Claude key from Drive
        // via the existing device account (drive.readonly is now in the token scope). No local key — the key
        // is published once by Storyboarder and read from Drive here. Degrades to neutral when unavailable.
        static Func<CancellationToken, Task<VoiceDriveService>> VoiceDriveFactory(IServiceProvider sp)
        {
            var tokens = sp.GetRequiredService<IGoogleAccessTokenProvider>();
            return async _ =>
            {
                var token = await tokens.GetAccessToken();
                return new VoiceDriveService(new VoiceClientInitializer
                {
                    HttpClientInitializer = VoiceGoogleCredential.FromAccessToken(token),
                    ApplicationName = "Foreword",
                    HttpClientFactory = new AndroidGoogleHttpClientFactory()
                });
            };
        }
        // Drive-by-ID (#13): shared, vault-seeded cache of well-known-file-name -> stable Drive id so a
        // renamed/moved file is still found by id. VaultSync seeds it on restore, snapshots it on publish.
        // Persist captured Drive ids to local Preferences on change (not only on the next vault publish),
        // seeding from there at startup, so a renamed file survives a restart instead of duplicating.
        builder.Services.AddSingleton<InMemoryDriveFileIdStore>(_ =>
        {
            var seed = DriveFileIdMapCodec.Decode(
                Microsoft.Maui.Storage.Preferences.Default.Get("driveFileIds", string.Empty));
            return new InMemoryDriveFileIdStore(seed, map =>
                Microsoft.Maui.Storage.Preferences.Default.Set("driveFileIds", DriveFileIdMapCodec.Encode(map)));
        });
        builder.Services.AddSingleton<IVoiceProfileStore>(sp =>
            new DriveVoiceProfileStore(new DriveTextFile(
                VoiceDriveFactory(sp), DriveVoiceProfileStore.FileName, sp.GetRequiredService<InMemoryDriveFileIdStore>())));

        // The cross-app encrypted secrets vault — ONE Drive file holding every app's keys, encrypted with
        // the owner's passphrase (CredentialStore.VaultPassphrase). Foreword publishes its own credentials
        // to it (VaultSync) and sources the shared Claude voice key from it, so secrets live in one place
        // (in place of the old plaintext voice config).
        builder.Services.AddSingleton<ISecretsVaultStore>(sp =>
            new DriveSecretsVaultStore(new DriveTextFile(
                VoiceDriveFactory(sp), DriveSecretsVaultStore.FileName, sp.GetRequiredService<InMemoryDriveFileIdStore>())));
        builder.Services.AddSingleton(sp =>
            new SecretsVaultService(sp.GetRequiredService<ISecretsVaultStore>(), () => CredentialStore.VaultPassphrase));
        builder.Services.AddSingleton<VaultSync>();

        // The private prayer archive: its own Drive file, encrypted with the same passphrase the
        // vault uses. A separate file rather than a section of the vault, because the vault is read
        // by every app in the suite and this is read by one.
        builder.Services.AddSingleton<IPrayerArchive>(sp =>
            new DrivePrayerArchive(new DriveTextFile(
                VoiceDriveFactory(sp), DrivePrayerArchive.FileName,
                sp.GetRequiredService<InMemoryDriveFileIdStore>())));
        builder.Services.AddTransient<PrayerSettings>();
        builder.Services.AddTransient<PrayerDeliveryService>(sp => new PrayerDeliveryService(
            sp.GetRequiredService<IPrayerArchive>(),
            sp.GetRequiredService<IJournalTranscriber>(),
            sp.GetRequiredService<IPastoralComposer>(),
            sp.GetRequiredService<IPrayerPdfComposer>(),
            sp.GetRequiredService<IRemarkableClient>(),
            () => CredentialStore.VaultPassphrase,
            sp.GetRequiredService<ILogger<PrayerDeliveryService>>()));
        builder.Services.AddSingleton<IVoiceConfigStore>(sp =>
            new VaultVoiceConfigStore(sp.GetRequiredService<ISecretsVaultStore>(), () => CredentialStore.VaultPassphrase));
        builder.Services.AddSingleton<DriveVoiceOptions>();
        builder.Services.AddSingleton<IOptions<VoiceOptions>>(sp => sp.GetRequiredService<DriveVoiceOptions>());
        builder.Services.AddHttpClient<IVoiceClient, VoiceClient>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromMinutes(2))
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddTransient<VoicedMessenger>();

        builder.Services.AddTransient<Pages.MainPage>();
        builder.Services.AddTransient<Pages.SetupPage>();
        builder.Services.AddTransient<Pages.RotationsPage>();

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
