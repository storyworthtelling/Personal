using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NytRemarkable.Domain.Interfaces;
using NytRemarkable.Infrastructure.Nyt;
using NytRemarkable.Infrastructure.Remarkable;
using NytRemarkable.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<NytOptions>(builder.Configuration.GetSection(NytOptions.SectionName));
builder.Services.Configure<RemarkableOptions>(builder.Configuration.GetSection(RemarkableOptions.SectionName));
builder.Services.Configure<ScheduleOptions>(builder.Configuration.GetSection(ScheduleOptions.SectionName));

builder.Services.AddHttpClient<INytCrosswordClient, NytCrosswordClient>();
builder.Services.AddHttpClient<IRemarkableAuthProvider, RemarkableAuthService>();
builder.Services.AddHttpClient<IRemarkableClient, RemarkableStorageClient>();

builder.Services.AddTransient<CrosswordDeliveryService>();
builder.Services.AddHostedService<CrosswordScheduler>();

var host = builder.Build();

if (args.Contains("--setup"))
{
    await RemarkableSetup.RegisterDevice(host.Services);
    return;
}

host.Run();
