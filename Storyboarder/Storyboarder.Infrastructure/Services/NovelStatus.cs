namespace Storyboarder.Infrastructure.Services;

public sealed record PhaseProgress(int Done, int Total);

public sealed record VolumeStatus(int Number, string Title, bool PdfReady);

// A read model summarizing a novel's progress for the status page / API. Every long-running stage
// carries a Done/Total so the card can say what the worker is on and how much of it is left:
// Chunks + ReferenceSheets cover the design-bible stage, Storyboard the storyboarding stage,
// Panels/Pages/Volumes the render-and-bind stages, and AnalysisNotes the map step of the report.
public sealed record NovelStatus(
    Guid Id,
    string Title,
    string Stage,
    string? LastError,
    PhaseProgress Chunks,
    PhaseProgress ReferenceSheets,
    PhaseProgress Storyboard,
    PhaseProgress Panels,
    PhaseProgress Pages,
    PhaseProgress Volumes,
    IReadOnlyList<VolumeStatus> VolumeList,
    PhaseProgress AnalysisNotes,
    PhaseProgress Analysis,
    bool AnalysisReady,
    string? CoverThumbPath = null);
