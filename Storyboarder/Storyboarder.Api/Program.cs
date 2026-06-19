using Microsoft.EntityFrameworkCore;
using Storyboarder.Api;
using Storyboarder.Api.Hubs;
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

var builder = WebApplication.CreateBuilder(args);

// Options (bound from appsettings / user-secrets / environment variables).
builder.Services.Configure<ClaudeOptions>(builder.Configuration.GetSection(ClaudeOptions.SectionName));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<GoogleOptions>(builder.Configuration.GetSection(GoogleOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));
builder.Services.Configure<PageLayoutOptions>(builder.Configuration.GetSection(PageLayoutOptions.SectionName));

var storage = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();

// Data access: a DbContext factory so both the background worker and request-scoped services can
// create contexts freely (no shared, captured DbContext).
builder.Services.AddDbContextFactory<StoryboarderDbContext>(options =>
    options.UseSqlite($"Data Source={storage.DatabasePath}"));

// External clients and infrastructure collaborators.
builder.Services.AddHttpClient<IClaudeAnalysisClient, ClaudeAnalysisClient>();
builder.Services.AddHttpClient<IGeminiImageClient, GeminiImageClient>();
builder.Services.AddSingleton<IGoogleDocsClient, GoogleDocsClient>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IImageStore, FileSystemImageStore>();
builder.Services.AddSingleton<IPageComposer, SkiaPageComposer>();
builder.Services.AddSingleton<IVolumePdfComposer, PdfVolumeComposer>();

// Pipeline + application services.
builder.Services.AddSingleton<GraphicNovelProcessor>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddHostedService<GraphicNovelWorker>();

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Apply migrations at startup so the SQLite schema exists.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<StoryboarderDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapHub<ProgressHub>("/hubs/progress");

app.Run();

// Exposed for WebApplicationFactory in the integration tests.
public partial class Program;
