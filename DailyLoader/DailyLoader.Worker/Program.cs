using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using DailyLoader.Domain.Interfaces;
using DailyLoader.Infrastructure.Calendar;
using DailyLoader.Infrastructure.Delivery;
using DailyLoader.Infrastructure.Google;
using DailyLoader.Infrastructure.Handwriting;
using DailyLoader.Infrastructure.Nyt;
using DailyLoader.Infrastructure.Pdf;
using DailyLoader.Infrastructure.Remarkable;
using DailyLoader.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<NytOptions>(builder.Configuration.GetSection(NytOptions.SectionName));
builder.Services.Configure<NytApiOptions>(builder.Configuration.GetSection(NytApiOptions.SectionName));
builder.Services.Configure<RemarkableOptions>(builder.Configuration.GetSection(RemarkableOptions.SectionName));
builder.Services.Configure<ScheduleOptions>(builder.Configuration.GetSection(ScheduleOptions.SectionName));
builder.Services.Configure<GoogleOptions>(builder.Configuration.GetSection(GoogleOptions.SectionName));
builder.Services.Configure<CalendarMappingOptions>(builder.Configuration.GetSection(CalendarMappingOptions.SectionName));
builder.Services.Configure<WorkScheduleOptions>(builder.Configuration.GetSection(WorkScheduleOptions.SectionName));
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));

builder.Services.AddHttpClient<INytCrosswordClient, NytCrosswordClient>();
builder.Services.AddHttpClient<INytTopStoriesClient, NytTopStoriesClient>();
builder.Services.AddSingleton<IArticlePageFetcher, PlaywrightPageFetcher>();
builder.Services.AddHttpClient<INytArticleClient, NytArticleClient>();
builder.Services.AddHttpClient<IRemarkableAuthProvider, RemarkableAuthService>();
builder.Services.AddHttpClient<IRemarkableClient, RemarkableStorageClient>();

builder.Services.AddTransient<IPdfComposer, PdfComposer>();
builder.Services.AddTransient<CrosswordDeliveryService>();

// Calendar + checklist feature.
builder.Services.AddSingleton<IGoogleClientFactory, GoogleClientFactory>();
builder.Services.AddTransient<IGoogleCalendarClient, GoogleCalendarClient>();
builder.Services.AddTransient<IGoogleTasksClient, GoogleTasksClient>();
builder.Services.AddTransient<CalendarColumnMapper>();
builder.Services.AddTransient<WorkScheduleAugmenter>();
builder.Services.AddTransient<IOutlookSelector, OutlookSelector>();
builder.Services.AddTransient<ICalendarPdfComposer, CalendarPdfComposer>();
builder.Services.AddTransient<IChecklistPdfComposer, ChecklistPdfComposer>();
builder.Services.AddHttpClient<IHandwritingExtractor, ClaudeVisionHandwritingExtractor>();
builder.Services.AddTransient<DailyDeliveryService>();

builder.Services.AddHostedService<CrosswordScheduler>();

var host = builder.Build();

if (args.Contains("--setup"))
{
    await RemarkableSetup.RegisterDevice(host.Services);
    return;
}

if (args.Contains("--google-auth"))
{
    await GoogleSetup.Authorize(host.Services);
    return;
}

if (args.Contains("--deliver"))
{
    using var scope = host.Services.CreateScope();
    var crossword = scope.ServiceProvider.GetRequiredService<CrosswordDeliveryService>();
    var daily = scope.ServiceProvider.GetRequiredService<DailyDeliveryService>();
    var scheduleOptions = scope.ServiceProvider.GetRequiredService<IOptions<ScheduleOptions>>().Value;
    var timeZone = TimeZoneInfo.FindSystemTimeZoneById(scheduleOptions.TimeZoneId);
    var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone));

    await crossword.DeliverDailyPuzzle(today);
    await daily.DeliverDailyDocuments(today);

    if (ScheduleHelper.IsFirstSundayOfMonth(today))
        await crossword.DeliverBonusPuzzle(today);

    return;
}

host.Run();
