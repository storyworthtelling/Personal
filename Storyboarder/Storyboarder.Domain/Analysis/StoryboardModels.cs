namespace Storyboarder.Domain.Analysis;

// One panel in a storyboard: what to draw, what text to typeset over it, and which design-bible
// entities appear (so their reference sheets can be supplied to the image model).
public sealed record PanelDraft(
    string VisualPrompt,
    string? Dialogue,
    string? Caption,
    IReadOnlyList<string> ReferenceNames);

public sealed record PageDraft(int ChapterNumber, IReadOnlyList<PanelDraft> Panels);

public sealed record VolumeDraft(int Number, string Title, IReadOnlyList<PageDraft> Pages);

// The storyboard produced for one text window, plus an updated continuity summary the next
// window's call carries forward to keep numbering and narrative flow coherent.
public sealed record StoryboardSegment(
    IReadOnlyList<VolumeDraft> Volumes,
    string ContinuitySummary);
