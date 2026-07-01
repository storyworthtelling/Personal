using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
using Storyboarder.Android.Google;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Imaging;
using Storyboarder.Domain.Time;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.External;
using Storyboarder.Infrastructure.Imaging;
using Storyboarder.Infrastructure.Persistence;
using Storyboarder.Infrastructure.Pipeline;
using Storyboarder.Infrastructure.Services;
using Storyboarder.Infrastructure.Time;

namespace Storyboarder.Android;

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
        builder.UseMauiApp<App>();

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "storyboarder.db");
        var outputRoot = Path.Combine(FileSystem.AppDataDirectory, "output");

        // Options read live from Preferences. Only the API keys and storage paths vary on-device;
        // everything else keeps its code default.
        builder.Services.AddSingleton<IOptions<ClaudeOptions>>(
            _ => new LiveOptions<ClaudeOptions>(() => new ClaudeOptions { ApiKey = SettingsStore.ClaudeApiKey }));
        builder.Services.AddSingleton<IOptions<GeminiOptions>>(
            _ => new LiveOptions<GeminiOptions>(() => new GeminiOptions { ApiKey = SettingsStore.GeminiApiKey }));
        builder.Services.AddSingleton<IOptions<RateLimitOptions>>(
            _ => new LiveOptions<RateLimitOptions>(() => new RateLimitOptions()));
        builder.Services.AddSingleton<IOptions<PageLayoutOptions>>(
            _ => new LiveOptions<PageLayoutOptions>(() => new PageLayoutOptions()));
        builder.Services.AddSingleton<IOptions<StorageOptions>>(
            _ => new LiveOptions<StorageOptions>(() => new StorageOptions
            {
                DatabasePath = dbPath,
                OutputRoot = outputRoot
            }));

        // Data access: a context factory so the worker and request-scoped services create contexts freely.
        builder.Services.AddDbContextFactory<StoryboarderDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // External HTTP clients route through AndroidMessageHandler — SELinux blocks the raw sockets
        // the default SocketsHttpHandler opens in an Android app process.
        static Xamarin.Android.Net.AndroidMessageHandler AndroidHandler() => new();
        builder.Services.AddHttpClient<IClaudeAnalysisClient, ClaudeAnalysisClient>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);
        builder.Services.AddHttpClient<IGeminiImageClient, GeminiImageClient>()
            .ConfigurePrimaryHttpMessageHandler(AndroidHandler);

        // Infrastructure collaborators.
        builder.Services.AddSingleton<IGoogleDocsClient, GoogleDocsClient>();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IImageStore, FileSystemImageStore>();
        builder.Services.AddSingleton<IPageComposer, SkiaPageComposer>();
        builder.Services.AddSingleton<IVolumePdfComposer, PdfVolumeComposer>();
        builder.Services.AddSingleton<AnalysisDocumentRenderer>();
        builder.Services.AddSingleton<IAnalysisPdfComposer, AnalysisPdfComposer>();

        // Pipeline + application services.
        builder.Services.AddSingleton<GraphicNovelProcessor>();
        builder.Services.AddSingleton<IPipelineStep>(sp => sp.GetRequiredService<GraphicNovelProcessor>());
        builder.Services.AddSingleton<PipelineRunner>();
        builder.Services.AddScoped<ProjectService>();

        // Android-side Google helpers consumed by the New Project page.
        builder.Services.AddSingleton<DeviceAccountTokenProvider>();
        builder.Services.AddSingleton<DriveDocsLister>();

        // Pages.
        builder.Services.AddTransient<Pages.MainPage>();
        builder.Services.AddTransient<Pages.NewProjectPage>();
        builder.Services.AddTransient<Pages.SetupPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
