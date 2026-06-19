using Microsoft.AspNetCore.Mvc;
using Storyboarder.Infrastructure.Services;

namespace Storyboarder.Api.Controllers;

[ApiController]
[Route("api/projects")]
public sealed class ProjectsController : ControllerBase
{
    private readonly ProjectService projects;

    public ProjectsController(ProjectService projects)
    {
        this.projects = projects;
    }

    public sealed record CreateProjectRequest(
        string FileId, string FileName, string AccessToken, string? Title, string? ArtStyle);

    // Creates a project from a Google Doc chosen via the in-app Drive picker. The browser supplies the
    // file id and a short-lived OAuth access token; the doc text is fetched once here at ingest.
    [HttpPost]
    public async Task<ActionResult<NovelStatus>> Create(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FileId) || string.IsNullOrWhiteSpace(request.AccessToken))
            return BadRequest("fileId and accessToken are required.");

        var id = await projects.CreateFromDocAsync(
            request.FileId,
            string.IsNullOrWhiteSpace(request.FileName) ? "Untitled" : request.FileName,
            request.AccessToken,
            request.Title,
            request.ArtStyle,
            cancellationToken);

        var status = await projects.GetStatusAsync(id, cancellationToken);
        return CreatedAtAction(nameof(GetOne), new { id }, status);
    }

    [HttpGet]
    public async Task<IReadOnlyList<NovelStatus>> List(CancellationToken cancellationToken) =>
        await projects.ListAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NovelStatus>> GetOne(Guid id, CancellationToken cancellationToken)
    {
        var status = await projects.GetStatusAsync(id, cancellationToken);
        return status is null ? NotFound() : Ok(status);
    }

    [HttpGet("{id:guid}/volumes/{number:int}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, int number, CancellationToken cancellationToken)
    {
        var path = await projects.GetVolumePdfPathAsync(id, number, cancellationToken);
        if (path is null || !System.IO.File.Exists(path))
            return NotFound();

        var stream = System.IO.File.OpenRead(path);
        return File(stream, "application/pdf", $"volume-{number}.pdf");
    }
}
