using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NytRemarkable.Domain.Interfaces;
using NytRemarkable.Infrastructure.Delivery;
using NytRemarkable.Infrastructure.Nyt;
using NytRemarkable.Infrastructure.Pdf;
using NytRemarkable.Infrastructure.Remarkable;
using NytRemarkable.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<NytOptions>(builder.Configuration.GetSection(NytOptions.SectionName));
builder.Services.Configure<NytApiOptions>(builder.Configuration.GetSection(NytApiOptions.SectionName));
builder.Services.Configure<RemarkableOptions>(builder.Configuration.GetSection(RemarkableOptions.SectionName));
builder.Services.Configure<ScheduleOptions>(builder.Configuration.GetSection(ScheduleOptions.SectionName));

builder.Services.AddHttpClient<INytCrosswordClient, NytCrosswordClient>();
builder.Services.AddHttpClient<INytTopStoriesClient, NytTopStoriesClient>();
builder.Services.AddSingleton<IArticlePageFetcher, PlaywrightPageFetcher>();
builder.Services.AddHttpClient<INytArticleClient, NytArticleClient>();
builder.Services.AddHttpClient<IRemarkableAuthProvider, RemarkableAuthService>();
builder.Services.AddHttpClient<IRemarkableClient, RemarkableStorageClient>();

builder.Services.AddTransient<IPdfComposer, PdfComposer>();
builder.Services.AddTransient<CrosswordDeliveryService>();
builder.Services.AddHostedService<CrosswordScheduler>();

var host = builder.Build();

if (args.Contains("--setup"))
{
    await RemarkableSetup.RegisterDevice(host.Services);
    return;
}

if (args.Contains("--deliver"))
{
    using var scope = host.Services.CreateScope();
    var delivery = scope.ServiceProvider.GetRequiredService<CrosswordDeliveryService>();
    var scheduleOptions = scope.ServiceProvider.GetRequiredService<IOptions<ScheduleOptions>>().Value;
    var timeZone = TimeZoneInfo.FindSystemTimeZoneById(scheduleOptions.TimeZoneId);
    var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone));

    await delivery.DeliverDailyPuzzle(today);

    if (ScheduleHelper.IsFirstSundayOfMonth(today))
        await delivery.DeliverBonusPuzzle(today);

    return;
}

host.Run();
