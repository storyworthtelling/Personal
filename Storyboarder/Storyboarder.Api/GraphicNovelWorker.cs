using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Storyboarder.Api.Hubs;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.Pipeline;
using Storyboarder.Infrastructure.Services;

namespace Storyboarder.Api;

// The continuously-running background worker: repeatedly asks the processor to advance the next due
// unit of work, broadcasting progress over SignalR. It paces itself off the processor's result
// (immediate on DidWork, the requested wait on Waiting, an idle poll when there's nothing to do) and
// never tight-loops.
public sealed class GraphicNovelWorker : BackgroundService
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(1);

    private readonly GraphicNovelProcessor processor;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IHubContext<ProgressHub> hub;
    private readonly TimeSpan idlePoll;
    private readonly ILogger<GraphicNovelWorker> logger;

    public GraphicNovelWorker(
        GraphicNovelProcessor processor,
        IServiceScopeFactory scopeFactory,
        IHubContext<ProgressHub> hub,
        IOptions<RateLimitOptions> rateLimitOptions,
        ILogger<GraphicNovelWorker> logger)
    {
        this.processor = processor;
        this.scopeFactory = scopeFactory;
        this.hub = hub;
        this.logger = logger;
        idlePoll = TimeSpan.FromSeconds(Math.Max(1, rateLimitOptions.Value.IdlePollSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ProcessResult result;
            try
            {
                result = await processor.ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Processor iteration failed; backing off.");
                await Wait(idlePoll, stoppingToken);
                continue;
            }

            if (result.NovelId is { } novelId && result.Outcome != ProcessOutcome.Idle)
                await Broadcast(novelId, stoppingToken);

            switch (result.Outcome)
            {
                case ProcessOutcome.DidWork:
                    break; // loop immediately
                case ProcessOutcome.Waiting:
                    await Wait(Clamp(result.Wait), stoppingToken);
                    break;
                default:
                    await Wait(idlePoll, stoppingToken);
                    break;
            }
        }
    }

    private async Task Broadcast(Guid novelId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var projects = scope.ServiceProvider.GetRequiredService<ProjectService>();
            var status = await projects.GetStatusAsync(novelId, cancellationToken);
            if (status is not null)
                await hub.Clients.All.SendAsync("ProjectUpdated", status, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to broadcast progress for {NovelId}", novelId);
        }
    }

    private static TimeSpan Clamp(TimeSpan wait)
    {
        if (wait < TimeSpan.FromSeconds(1))
            return TimeSpan.FromSeconds(1);
        return wait > MaxWait ? MaxWait : wait;
    }

    private static async Task Wait(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
