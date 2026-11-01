using Storyworthtelling.Diagnostics;
using Folio.Domain.Diagnostics;
using Storyworthtelling.Maui.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
using Folio.Android.Google;
using Folio.Domain.External;
using Folio.Domain.Imaging;
using Folio.Domain.Time;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.External;
using Folio.Infrastructure.Imaging;
using Folio.Infrastructure.Persistence;
using Folio.Infrastructure.Pipeline;
using Folio.Infrastructure.Services;
using Folio.Infrastructure.Time;
using Storyworthtelling.Paper;
using Storyworthtelling.Remarkable;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;
using GoogleCredential = global::Google.Apis.Auth.OAuth2.GoogleCredential;
using GoogleClientInitializer = global::Google.Apis.Services.BaseClientService.Initializer;
using DriveService = global::Google.Apis.Drive.v3.DriveService;

namespace Folio.Android;

public static class MauiProgram
{
    // IOptions<T> caches its Configure delegate; this wrapper re-reads SettingsStore on every access
    // so the background worker always sees the current API keys without an app restart.
    private sealed class LiveOptions<T>(Func<T> factory) : IOptions<T> where T : class
    {
        public T Value => factory();
    }

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
            // All eight faces, from the kit. This head registered six of them by hand and shipped the
            // other two — Serif Black, which DESIGN.md §4 gives every screen header, and Pinyon
            // Script, the owner's voice — with no alias at all. Neither failed: MAUI substitutes the
            // system face for an unknown family and reports nothing.
            .ConfigureFonts(Storyworthtelling.Maui.PaperFonts.Register);

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "folio.db");
        var outputRoot = Path.Combine(FileSystem.AppDataDirectory, "output");

        // Options read live from Preferences. Only the API keys and storage paths vary on-device;
        // everything else keeps its code default.
        builder.Services.AddSingleton<IOptions<ClaudeOptions>>(
            _ => new LiveOptions<ClaudeOptions>(() => new ClaudeOptions { ApiKey = SettingsStore.ClaudeApiKey }));
        builder.Services.AddSingleton<IOptions<GeminiOptions>>(
            _ => new LiveOptions<GeminiOptions>(() => new GeminiOptions { ApiKey = SettingsStore.GeminiApiKey }));
        builder.Services.AddSingleton<IOptions<RateLimitOptions>>(
            _ => new LiveOptions<RateLimitOptions>(() => new RateLimitOptions()));
        // Read live from Preferences like the keys are, so switching archiving on takes effect on the
        // next worker cycle without a restart.
        builder.Services.AddSingleton<IOptions<ArchiveOptions>>(
            _ => new LiveOptions<ArchiveOptions>(() => new ArchiveOptions
            {
                Enabled = SettingsStore.ArchiveToDrive,
                FreeLocalCopies = SettingsStore.FreeLocalCopiesAfterArchive,
                DeliverToRemarkable = SettingsStore.DeliverToRemarkable
            }));
        // The reMarkable device token from the one-time pairing, read live so a re-pair takes effect on the
        // next worker cycle without a restart.
        builder.Services.AddSingleton<IOptions<RemarkableOptions>>(
            _ => new LiveOptions<RemarkableOptions>(() => new RemarkableOptions { DeviceToken = SettingsStore.RemarkableDeviceToken }));
        builder.Services.AddSingleton<IOptions<StorageOptions>>(
            _ => new LiveOptions<StorageOptions>(() => new StorageOptions
            {
                DatabasePath = dbPath,
                OutputRoot = outputRoot
            }));
        // The owner's-voice API key is the Claude key, read live so entering it in Settings takes effect
        // on the next call without a restart (the singleton worker holds one VoiceClient, which reads
        // these options fresh on every call).
        builder.Services.AddSingleton<IOptions<VoiceOptions>>(
            _ => new LiveOptions<VoiceOptions>(() => new VoiceOptions { ApiKey = SettingsStore.ClaudeApiKey }));

        // Data access: a context factory so the worker and request-scoped services create contexts freely.
        builder.Services.AddDbContextFactory<FolioDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // External HTTP clients route through AndroidMessageHandler — SELinux blocks the raw sockets
        // the default SocketsHttpHandler opens in an Android app process.
        static Xamarin.Android.Net.AndroidMessageHandler AndroidHandler() => new();
        // The default 100s HttpClient timeout is shorter than a large Claude generation or a slow Gemini
        // render; both would abort mid-flight. Claude responses are streamed so the connection stays warm,
        // but a generous ceiling still guards against a genuinely stuck request.
        // Credential health: the API clients report Ok/Failing here (injected as ICredentialHealthSink),
        // and the Settings page reads the report back to hide a working key's field. Registered before the
        // typed clients so ActivatorUtilities injects it into their optional sink parameter.
        builder.Services.AddSingleton<CredentialHealthStore>();
        builder.Services.AddSingleton<ICredentialHealthSink>(sp => sp.GetRequiredService<CredentialHealthStore>());

        builder.Services.AddHttpClient<IClaudeAnalysisClient, ClaudeAnalysisClient>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromMinutes(10))
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddHttpClient<IGeminiImageClient, GeminiImageClient>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromMinutes(5))
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        // The owner's-voice client. Its key is the same Claude key, read live from Preferences so it
        // tracks the Settings page like every other credential. Short calls (a distilled profile, a
        // one-sentence message), so a 2-minute ceiling is ample.
        builder.Services.AddHttpClient<IVoiceClient, VoiceClient>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromMinutes(2))
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        // The shared reMarkable delivery client (Phase 4): the auth service exchanges the paired device
        // token for a session JWT (reporting its health), the storage client uploads the report into the
        // tablet, and the registration client handles the one-time device-code pairing.
        builder.Services.AddHttpClient<IRemarkableAuthProvider, RemarkableAuthService>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddHttpClient<IRemarkableClient, RemarkableStorageClient>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromMinutes(5))
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddHttpClient<IRemarkableRegistration, RemarkableRegistrationClient>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);

        // Infrastructure collaborators.
        builder.Services.AddSingleton<IGoogleDocsClient, GoogleDocsClient>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IImageStore, FileSystemImageStore>();
        // The image-to-PDF assembler is gone from this head: the report draws straight into a
        // VectorPdfSink now, and nothing else here wraps pictures of pages.
        builder.Services.AddSingleton<AnalysisDocumentRenderer>();
        builder.Services.AddSingleton<IAnalysisPdfComposer, AnalysisPdfComposer>();

        // Pipeline + application services.
        builder.Services.AddSingleton<ReviewProcessor>();
        builder.Services.AddSingleton<IPipelineStep>(sp => sp.GetRequiredService<ReviewProcessor>());
        builder.Services.AddSingleton<PipelineRunner>();
        builder.Services.AddScoped<ProjectService>();
        builder.Services.AddSingleton<ProjectBackupService>();

        // Android-side Google helpers consumed by the New Project page.
        builder.Services.AddSingleton<DeviceAccountTokenProvider>();
        builder.Services.AddSingleton<DriveDocsLister>();
        builder.Services.AddSingleton<IPaperDriveArchive, DeviceAccountDriveArchive>();

        // The unified delivery seam: compose the reMarkable transport (root upload) and the Drive archive so
        // the pipeline delivers through one interface. Either may be absent — PaperDelivery reports that via
        // its capability flags and the archive stage skips that target.
        builder.Services.AddSingleton<IPaperDelivery>(sp =>
            new PaperDelivery(sp.GetService<IRemarkableClient>(), sp.GetService<IPaperDriveArchive>()));

        // Owner's-voice Drive files, each ONE JSON file found by name and reached with the device-account
        // token (full `drive` scope, shared across every com.storyworthtelling app) the same way the archive
        // is: the voice PROFILE (the pipeline writes it at ingestion; VoicedMessenger reads it for greetings)
        // and the shared CONFIG carrying the Claude key (this app's settings publish it so the other apps
        // read the key from Drive rather than each keeping their own).
        static Func<CancellationToken, Task<DriveService>> DriveFactory(IServiceProvider sp)
        {
            var tokens = sp.GetRequiredService<DeviceAccountTokenProvider>();
            return async _ =>
            {
                var token = await tokens.GetAccessToken();
                return new DriveService(new GoogleClientInitializer
                {
                    HttpClientInitializer = GoogleCredential.FromAccessToken(token),
                    ApplicationName = "Folio",
                    HttpClientFactory = new AndroidGoogleHttpClientFactory()
                });
            };
        }
        // Drive-by-ID (#13): a shared, vault-seeded cache of well-known-file-name -> stable Drive id, so a
        // renamed/moved file is still found by id. VaultSync seeds it on restore and snapshots it on publish.
        // Persist captured ids to local Preferences the moment they change (not only on the next vault
        // publish), and seed from there at startup — so a file the owner renamed between sessions is still
        // found by id instead of being re-created as a duplicate.
        builder.Services.AddSingleton<InMemoryDriveFileIdStore>(_ =>
        {
            var seed = DriveFileIdMapCodec.Decode(
                Microsoft.Maui.Storage.Preferences.Default.Get("driveFileIds", string.Empty));
            return new InMemoryDriveFileIdStore(seed, map =>
                Microsoft.Maui.Storage.Preferences.Default.Set("driveFileIds", DriveFileIdMapCodec.Encode(map)));
        });
        builder.Services.AddSingleton<IVoiceProfileStore>(sp =>
            new DriveVoiceProfileStore(new DriveTextFile(
                DriveFactory(sp), DriveVoiceProfileStore.FileName, sp.GetRequiredService<InMemoryDriveFileIdStore>())));

        // The cross-app encrypted secrets vault — ONE Drive file holding every app's keys, encrypted with
        // the owner's passphrase (SettingsStore.VaultPassphrase). Folio both publishes its own keys to
        // it (VaultSync) and, being the app the shared Claude key is entered in, sources the voice key from it
        // (VaultVoiceConfigStore) so there is a single source of truth in place of the old plaintext config.
        builder.Services.AddSingleton<ISecretsVaultStore>(sp =>
            new DriveSecretsVaultStore(new DriveTextFile(
                DriveFactory(sp), DriveSecretsVaultStore.FileName, sp.GetRequiredService<InMemoryDriveFileIdStore>())));
        builder.Services.AddSingleton(sp =>
            new SecretsVaultService(sp.GetRequiredService<ISecretsVaultStore>(), () => SettingsStore.VaultPassphrase));
        builder.Services.AddSingleton<VaultSync>();
        builder.Services.AddSingleton<IVoiceConfigStore>(sp =>
            new VaultVoiceConfigStore(sp.GetRequiredService<ISecretsVaultStore>(), () => SettingsStore.VaultPassphrase));
        builder.Services.AddTransient<VoicedMessenger>();

        // Pages.
        builder.Services.AddTransient<Pages.MainPage>();
        builder.Services.AddTransient<Pages.NewProjectPage>();
        builder.Services.AddTransient<Pages.SetupPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        // Persist the pipeline's log to a file so it can be viewed/shared from Settings for diagnosis.
        // The pipeline log (LOG-LOOP-PLAN.md §4.4): Folio's own categories from Information, sealed into the log
        // loop's outbox when full. A message is cut at 2 KB because a provider's error body can quote the manuscript.
        builder.Logging.AddProvider(MauiLog.Install(new FileLoggerOptions(
            FileSystem.AppDataDirectory, "pipeline.log", 1_500_000,
            category => category.StartsWith("Folio", StringComparison.Ordinal),
            MaxMessageChars: 2048)));

        // The log loop (LOG-LOOP-PLAN.md §3, §4): the tap watch, then — once the services exist — the launch line,
        // exit reasons and the Drive upload the head's worker runs inside the Saturday window.
        MauiTapWatch.Install(MauiLogLoop.Tap);
        var app = builder.Build();
        MauiLogLoop.Install(new MauiLoopSetup(
            "folio", "Folio", FolioKeyActions.SendLogsNow,
            DriveFactory(app.Services),
            () => SettingsStore.VaultPassphrase,
            _ => Task.FromResult(LogPhone.Owner)));
        return app;
    }
}
