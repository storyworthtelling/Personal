using Microsoft.AspNetCore.SignalR;

namespace Storyboarder.Api.Hubs;

// Clients connect here to receive "ProjectUpdated" pushes as the worker advances novels.
// The server broadcasts via IHubContext; there are no client-callable methods.
public sealed class ProgressHub : Hub
{
}
