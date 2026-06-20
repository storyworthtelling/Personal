namespace Storyboarder.Infrastructure.Services;

public sealed record PhaseProgress(int Done, int Total);

public sealed record VolumeStatus(int Number, string Title, bool PdfReady);

// A read model summarizing a novel's progress for the status page / API.
public sealed record NovelStatus(
    Guid Id,
    string Title,
    string Stage,
    string? LastError,
    PhaseProgress Chunks,
    PhaseProgress ReferenceSheets,
    PhaseProgress Panels,
    PhaseProgress Pages,
    PhaseProgress Volumes,
    IReadOnlyList<VolumeStatus> VolumeList);
