using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
using Ledger.Domain.External;
using Ledger.Domain.Time;
using Ledger.Infrastructure.Configuration;
using Ledger.Infrastructure.External;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Pipeline;
using Ledger.Infrastructure.Services;
using Ledger.Infrastructure.Time;
using Ledger.Android.Google;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;
using GoogleCredential = global::Google.Apis.Auth.OAuth2.GoogleCredential;
using GoogleClientInitializer = global::Google.Apis.Services.BaseClientService.Initializer;
using DriveService = global::Google.Apis.Drive.v3.DriveService;

namespace Ledger.Android;

public static class MauiProgram
{
    // IOptions<T> caches its Configure delegate; this wrapper re-reads SettingsStore on every access so
    // the background worker always sees the current Plaid credentials without an app restart.
    private sealed class LiveOptions<T>(Func<T> factory) : IOptions<T> where T : class
    {
        public T Value => factory();
    }

    public static MauiApp CreateMauiApp()
    {
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

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "ledger.db");

        // Options read live from Preferences: the Plaid credentials + environment vary on-device;
        // everything else keeps its code default.
        builder.Services.AddSingleton<IOptions<PlaidOptions>>(
            _ => new LiveOptions<PlaidOptions>(() => new PlaidOptions
            {
                ClientId = SettingsStore.PlaidClientId,
                Secret = SettingsStore.PlaidSecret,
                Environment = SettingsStore.PlaidEnvironment
            }));
        builder.Services.AddSingleton<IOptions<RefreshOptions>>(
            _ => new LiveOptions<RefreshOptions>(() => new RefreshOptions()));

        // Data access: a context factory so the worker and request-scoped services create contexts freely.
        builder.Services.AddDbContextFactory<LedgerDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // The Plaid client routes through AndroidMessageHandler — SELinux blocks the raw sockets the
        // default SocketsHttpHandler opens in an Android app process.
        static Xamarin.Android.Net.AndroidMessageHandler AndroidHandler() => new();

        // Credential health: the Plaid client reports Ok/Failing here (injected as ICredentialHealthSink),
        // and the Settings page reads it back to hide the credential fields while they're working. Registered
        // before the typed client so ActivatorUtilities injects it into the optional sink parameter.
        builder.Services.AddSingleton<CredentialHealthStore>();
        builder.Services.AddSingleton<ICredentialHealthSink>(sp => sp.GetRequiredService<CredentialHealthStore>());

        builder.Services.AddHttpClient<IPlaidClient, PlaidClient>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);

        // Infrastructure collaborators + pipeline.
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<BalanceRefreshProcessor>();
        builder.Services.AddSingleton<IPipelineStep>(sp => sp.GetRequiredService<BalanceRefreshProcessor>());
        builder.Services.AddSingleton<PipelineRunner>();
        builder.Services.AddScoped<AccountService>();

        // Owner's voice: read the shared profile + Claude key from Drive (device-account sign-in, read-only)
        // and greet the owner in their voice on the account list, degrading to neutral copy when the profile,
        // key, or Drive access isn't there. The key is published by Storyboarder's settings; Ledger keeps no
        // key of its own.
        builder.Services.AddSingleton<DeviceAccountTokenProvider>();
        static Func<CancellationToken, Task<DriveService>> DriveFactory(IServiceProvider sp)
        {
            var tokens = sp.GetRequiredService<DeviceAccountTokenProvider>();
            return async _ =>
            {
                var token = await tokens.GetAccessToken();
                return new DriveService(new GoogleClientInitializer
                {
                    HttpClientInitializer = GoogleCredential.FromAccessToken(token),
                    ApplicationName = "Ledger",
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
                DriveFactory(sp), DriveVoiceProfileStore.FileName, sp.GetRequiredService<InMemoryDriveFileIdStore>())));

        // The cross-app encrypted secrets vault — ONE Drive file holding every app's keys, encrypted with
        // the owner's passphrase (SettingsStore.VaultPassphrase). Ledger publishes its Plaid credentials and
        // each bank's access token to it (VaultSync) and sources the shared Claude voice key from it, so the
        // vault is the single place secrets live (in place of the old plaintext voice config).
        builder.Services.AddSingleton<ISecretsVaultStore>(sp =>
            new DriveSecretsVaultStore(new DriveTextFile(
                DriveFactory(sp), DriveSecretsVaultStore.FileName, sp.GetRequiredService<InMemoryDriveFileIdStore>())));
        builder.Services.AddSingleton(sp =>
            new SecretsVaultService(sp.GetRequiredService<ISecretsVaultStore>(), () => SettingsStore.VaultPassphrase));
        builder.Services.AddScoped<VaultSync>();
        builder.Services.AddSingleton<IVoiceConfigStore>(sp =>
            new VaultVoiceConfigStore(sp.GetRequiredService<ISecretsVaultStore>(), () => SettingsStore.VaultPassphrase));
        // One DriveVoiceOptions instance is both the IOptions<VoiceOptions> VoiceClient reads and the handle
        // the page refreshes (pull the key from Drive) just before composing.
        builder.Services.AddSingleton<DriveVoiceOptions>();
        builder.Services.AddSingleton<IOptions<VoiceOptions>>(sp => sp.GetRequiredService<DriveVoiceOptions>());
        builder.Services.AddHttpClient<IVoiceClient, VoiceClient>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromMinutes(2))
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddTransient<VoicedMessenger>();

        // Pages.
        builder.Services.AddTransient<Pages.MainPage>();
        builder.Services.AddTransient<Pages.SetupPage>();
        builder.Services.AddTransient<Pages.PlaidLinkPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        // Persist the refresh log to a file so it can be viewed/shared from Settings for diagnosis.
        builder.Logging.AddProvider(PipelineFileLogger.Instance);

        return builder.Build();
    }
}
