using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Storyboarder.Infrastructure.Configuration;

namespace Storyboarder.Api.Controllers;

[ApiController]
[Route("api/config")]
public sealed class ConfigController : ControllerBase
{
    private readonly GoogleOptions google;

    public ConfigController(IOptions<GoogleOptions> google)
    {
        this.google = google.Value;
    }

    // Non-secret Google client id + browser API key the status page needs to launch the Drive picker.
    [HttpGet("google")]
    public object Google() => new { clientId = google.ClientId, apiKey = google.ApiKey };
}
