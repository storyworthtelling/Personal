using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
using Index.Domain.External;
using Index.Domain.Health;
using Index.Domain.Identity;
using Index.Domain.Household;
using Index.Domain.Model;
using Index.Domain.Time;
using Index.Infrastructure.Configuration;
using Index.Infrastructure.External;
using Index.Infrastructure.Household;
using Index.Infrastructure.Persistence;
using Index.Infrastructure.Pipeline;
using Index.Infrastructure.Services;
using Index.Infrastructure.Snowball;
using Index.Infrastructure.Time;
using Index.Infrastructure.Usage;
using Index.Infrastructure.Voice;
using Index.Infrastructure.Worksheet;
using Index.Android.Google;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Remarkable;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;
using GoogleCredential = global::Google.Apis.Auth.OAuth2.GoogleCredential;
using GoogleClientInitializer = global::Google.Apis.Services.BaseClientService.Initializer;
using DriveService = global::Google.Apis.Drive.v3.DriveService;

namespace Index.Android;

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
            // All eight faces, from the kit. This head registered six of them by hand and shipped
            // the other two — Serif Black, which DESIGN.md §4 gives every screen header, and Pinyon
            // Script, the owner's voice — with no alias at all. Neither failed: MAUI substitutes
            // the system face for an unknown family and reports nothing.
            .ConfigureFonts(Storyworthtelling.Maui.PaperFonts.Register);

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "ledger.db");

        // Options read live from Preferences: the Plaid credentials vary on-device; everything else
        // keeps its code default. Deliberately NOT wired here: PlaidOptions.Environment. Index talks
        // to Plaid's production environment only (D144) — there is no environment setting in the
        // app, so nothing stored in Preferences (or restored from an old Ledger vault by VaultSync)
        // can ever pick a different host. PlaidOptions.Environment keeps its own "production" default
        // for that reason; a developer testing against Plaid's sandbox locally edits that one default.
        builder.Services.AddSingleton<IOptions<PlaidOptions>>(
            _ => new LiveOptions<PlaidOptions>(() => new PlaidOptions
            {
                ClientId = SettingsStore.PlaidClientId,
                Secret = SettingsStore.PlaidSecret
            }));
        builder.Services.AddSingleton<IOptions<RefreshOptions>>(
            _ => new LiveOptions<RefreshOptions>(() => new RefreshOptions()));

        // Release F: the reMarkable device token (shared, restored via VaultSync from whichever app
        // paired first — Index has no pairing UI of its own) and the shared Claude key (already read
        // from Drive by DriveVoiceOptions below, whose registration this factory reads live from).
        builder.Services.AddSingleton<IOptions<RemarkableOptions>>(
            _ => new LiveOptions<RemarkableOptions>(() => new RemarkableOptions { DeviceToken = SettingsStore.RemarkableDeviceToken }));

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
        builder.Services.AddScoped<SnowballService>();
        builder.Services.AddScoped<BudgetService>();
        builder.Services.AddScoped<SavingsService>();

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
                    ApplicationName = IndexIdentity.DisplayName,
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

        // Release I (D10, finding §3): the household snapshot the owner's install publishes and hers
        // reads — its own Drive file, over the same seam every other Drive file here uses. One shared
        // IDriveTextFile instance because the publisher and the reader address the exact same file.
        builder.Services.AddSingleton<IDriveTextFile>(sp =>
            new DriveTextFile(
                DriveFactory(sp), HouseholdSnapshotReader.FileName, sp.GetRequiredService<InMemoryDriveFileIdStore>()));
        builder.Services.AddScoped<HouseholdSnapshotPublisher>();
        builder.Services.AddScoped<HouseholdSnapshotReader>();

        // Which install this is, settled ONCE from the household file and remembered in the local
        // settings (Phase 1.1): launch, the workers, the vault restore and all five pages ask this
        // instead of each re-reading Drive. A singleton, because the answer is the device's.
        builder.Services.AddSingleton<IInstallRoleCache, PreferencesInstallRoleCache>();
        builder.Services.AddSingleton<IHouseholdPublisherLookup>(sp =>
            new HouseholdPublisherLookup(new HouseholdSnapshotReader(sp.GetRequiredService<IDriveTextFile>())));
        builder.Services.AddSingleton(sp =>
            new InstallRoleService(
                sp.GetRequiredService<IInstallRoleCache>(),
                sp.GetRequiredService<IHouseholdPublisherLookup>(),
                () => SettingsStore.GoogleAccountEmail,
                () => SettingsStore.VaultPassphrase));

        // The owner's recorded Debt Snowball history comes from THEIR Drive at runtime, sealed with the vault
        // passphrase — it is not in the repository or the APK. Its IDriveTextFile is constructed here for its
        // OWN file name and handed straight to the source: it must never be a second bare IDriveTextFile in
        // the container, because DI would then give the importer whichever registration came last (the
        // household file above). The role is read live so her install, which never reads it, is told apart
        // without a second Drive call. Guarded by SnowballHistoryNotInTheRepoTests.
        builder.Services.AddSingleton<ISnowballHistoryImportMemory, PreferencesSnowballHistoryImportMemory>();
        builder.Services.AddSingleton(sp =>
            new SnowballHistoryImport(
                new DriveSnowballHistorySource(
                    new DriveTextFile(
                        DriveFactory(sp), DriveSnowballHistorySource.FileName, sp.GetRequiredService<InMemoryDriveFileIdStore>())),
                () => SettingsStore.VaultPassphrase,
                () => sp.GetRequiredService<InstallRoleService>().Current,
                sp.GetRequiredService<ISnowballHistoryImportMemory>()));

        // Pulp's usage file (Pulp D320): the household's own passphrase-protected file, read by BOTH installs so the
        // line under a charge does not depend on Pulp living on the same phone. Its IDriveTextFile is built here for
        // its OWN file name and handed straight to the source, like the snowball history's, never resolved bare.
        builder.Services.AddSingleton(sp =>
            new DrivePulpUsageSource(
                new DriveTextFile(
                    DriveFactory(sp), DrivePulpUsageSource.FileName, sp.GetRequiredService<InMemoryDriveFileIdStore>()),
                sp.GetService<ILogger<DrivePulpUsageSource>>()));
        builder.Services.AddSingleton<PulpUsageSync>();

        // The cross-app encrypted secrets vault — ONE Drive file holding every app's keys, encrypted with
        // the owner's passphrase (SettingsStore.VaultPassphrase). Ledger publishes its Plaid credentials and
        // each bank's access token to it (VaultSync) and sources the shared Claude voice key from it, so the
        // vault is the single place secrets live (in place of the old plaintext voice config).
        builder.Services.AddSingleton<ISecretsVaultStore>(sp =>
            new DriveSecretsVaultStore(new DriveTextFile(
                DriveFactory(sp), DriveSecretsVaultStore.FileName, sp.GetRequiredService<InMemoryDriveFileIdStore>())));
        // Whether this install may write the vault is decided by the vault file's Drive ownership, read by the
        // store on every load: the admin's install owns the file, the other install has it shared with it.
        // Her blank Plaid section therefore cannot replace the owner's. Guarded by HouseholdGateSourceTests (R-G1).
        builder.Services.AddSingleton(sp =>
            new SecretsVaultService(
                sp.GetRequiredService<ISecretsVaultStore>(),
                () => SettingsStore.VaultPassphrase));
        builder.Services.AddScoped<VaultSync>();
        // One restore at a time, process-wide: the launch and a passphrase save both bring the vault's
        // banks back, and the Accounts page asks this gate whether banks are on their way (task 2.6).
        builder.Services.AddSingleton<RestoreGate>();
        builder.Services.AddSingleton<IOwnerWorkScheduler, OwnerWork>();
        builder.Services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("PassphraseRestore");
            return new PassphraseRestoreFlow(
                sp.GetRequiredService<InstallRoleService>(),
                sp.GetRequiredService<RestoreGate>(),
                sp.GetRequiredService<IOwnerWorkScheduler>(),
                ex => logger.LogWarning(ex, "Restoring from the vault after the passphrase was saved did not complete."));
        });
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
        // The three voice seats (remediation 3.7): Accounts, Snowball and Savings each ask this ONE composer
        // for a sentence built from their facts. A singleton so its once-a-day memory outlives any page. The
        // seat needs the same signed-in Google account the profile and the vault are read with; without one
        // it is hidden rather than asked.
        builder.Services.AddSingleton<VoiceSeatComposer>(sp => new VoiceSeatComposer(
            sp.GetRequiredService<VoicedMessenger>(),
            sp.GetRequiredService<DriveVoiceOptions>(),
            sp.GetRequiredService<IClock>(),
            () => !string.IsNullOrEmpty(SettingsStore.GoogleAccountEmail),
            sp.GetRequiredService<ILogger<VoiceSeatComposer>>()));

        // Release F (Index-P11): the worksheet's reMarkable delivery and hand-written read-back.
        builder.Services.AddHttpClient<IRemarkableAuthProvider, RemarkableAuthService>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddHttpClient<IRemarkableClient, RemarkableStorageClient>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);

        // The shared Claude key already lives in DriveVoiceOptions above (the same key Folio's
        // Settings publishes); ClaudeVisionOptions is the extractor's own options shape, matching
        // Foreword's own "same key, second IOptions<T> registration" idiom.
        builder.Services.AddSingleton<IOptions<ClaudeVisionOptions>>(sp =>
            new LiveOptions<ClaudeVisionOptions>(() => new ClaudeVisionOptions
            {
                ApiKey = sp.GetRequiredService<DriveVoiceOptions>().Value.ApiKey
            }));
        // A factory, not a bare AddHttpClient<IHandwritingExtractor, ...> registration: the
        // instruction string varies by month (WorksheetReadBackInstruction.Build depends on the
        // RECORDED category order), so WorksheetReadBackService builds a fresh extractor per read-back
        // call with that call's own instruction — see its own remarks for why.
        builder.Services.AddHttpClient("index-handwriting")
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddSingleton<Func<string, IHandwritingExtractor>>(sp => instruction =>
            new ClaudeVisionHandwritingExtractor(
                instruction,
                CredentialIds.Claude,
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("index-handwriting"),
                sp.GetRequiredService<IOptions<ClaudeVisionOptions>>(),
                sp.GetRequiredService<ILogger<ClaudeVisionHandwritingExtractor>>(),
                sp.GetService<ICredentialHealthSink>()));

        builder.Services.AddScoped<WorksheetPlanBuilder>();
        builder.Services.AddScoped<WorksheetReadBackService>();
        builder.Services.AddScoped<FollowUpPlanBuilder>();
        builder.Services.AddScoped<FollowUpReadBackService>();
        builder.Services.AddScoped<WorksheetDeliveryService>();

        // Pages.
        builder.Services.AddTransient<Pages.MainPage>();
        builder.Services.AddTransient<Pages.SetupPage>();
        builder.Services.AddTransient<Pages.PlaidLinkPage>();
        builder.Services.AddTransient<Pages.AddManualAccountPage>();
        builder.Services.AddTransient<Pages.SnowballPage>();
        builder.Services.AddTransient<Pages.BudgetPage>();
        builder.Services.AddTransient<Pages.SavingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        // Persist the refresh log to a file so it can be viewed/shared from Settings for diagnosis.
        builder.Logging.AddProvider(PipelineFileLogger.Instance);

        return builder.Build();
    }
}
