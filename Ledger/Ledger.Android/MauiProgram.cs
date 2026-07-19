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
        builder.Services.AddHttpClient<IPlaidClient, PlaidClient>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);

        // Infrastructure collaborators + pipeline.
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<BalanceRefreshProcessor>();
        builder.Services.AddSingleton<IPipelineStep>(sp => sp.GetRequiredService<BalanceRefreshProcessor>());
        builder.Services.AddSingleton<PipelineRunner>();
        builder.Services.AddScoped<AccountService>();

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
