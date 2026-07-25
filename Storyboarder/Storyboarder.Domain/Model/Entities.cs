using System.ComponentModel.DataAnnotations.Schema;

namespace Storyboarder.Domain.Model;

// Persistence entities (plain POCOs; EF configuration lives in the Infrastructure DbContext).
// Image bytes are kept on disk — these rows store paths, not blobs — and every unit of work
// carries a status so the background worker can resume purely from persisted state.

public class Novel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string GoogleDocId { get; set; } = string.Empty;
    public string GoogleDocName { get; set; } = string.Empty;
    public string ArtStyle { get; set; } = ArtStyles.Default;
    public ProjectStage Stage { get; set; } = ProjectStage.Created;

    // Draft versioning. A "project" is one book the owner is revising; each draft is a fresh conversion
    // of a re-picked Google Doc, all sharing one ProjectId. The first draft's ProjectId is its own Id
    // (so a lone novel is a one-draft project), DraftNumber is 1, and PreviousDraftId is null. A later
    // draft carries the same ProjectId, the next DraftNumber, and PreviousDraftId pointing at the draft
    // it was made from — which is what the diff and the incremental re-render read to reuse the panels
    // whose source text didn't move. Not a navigation property: drafts are queried by ProjectId, and a
    // self-referencing FK would drag the whole prior graph into the change-tracker on every save.
    public Guid ProjectId { get; set; }
    public int DraftNumber { get; set; } = 1;
    public Guid? PreviousDraftId { get; set; }

    // Report-only draft: skip every art stage (storyboarding, panel renders, page composition, volume
    // binding) and go straight from the design bible to the revision report. Not the default — the
    // illustration is the comprehension probe, so most drafts still want it — but a re-read of a draft
    // that has already been illustrated only needs the report, at a fraction of the cost and time.
    public bool ReportOnly { get; set; }

    // Set once the per-chunk extractions have been merged into canonical DesignRefs, so the merge
    // (which would otherwise reset reference-sheet state) runs exactly once.
    public bool DesignBibleConsolidated { get; set; }

    // Set once chapter boundaries have been detected and persisted at ingestion, so the (occasionally
    // Claude-assisted) detection runs exactly once per novel.
    public bool ChaptersDetected { get; set; }

    // Chunk ordinal up to which storyboarding has already run (for resumable, windowed storyboarding).
    public int StoryboardCursor { get; set; }

    // How many storyboard sub-windows of the CURRENT chunk (StoryboardCursor) are already done. A whole
    // chunk is too large to storyboard in one Claude call — the generation runs long enough that the
    // background worker is killed mid-stream — so each chunk is sliced into small windows and this
    // advances one window at a time, resetting to 0 when the chunk is finished and the cursor moves on.
    public int StoryboardWindowIndex { get; set; }

    public string ContinuitySummary { get; set; } = string.Empty;

    // Consecutive failed storyboard attempts for the CURRENT window. Bounded by RateLimitOptions
    // .MaxAttempts so a persistently-failing call can't retry (and re-bill) forever; reset to 0 on a
    // successful window or a manual retry.
    public int StoryboardAttempts { get; set; }

    public string? LastError { get; set; }

    // When the pipeline last deferred this novel because a provider rate-limited it (HTTP 429 /
    // quota), this is when it expects to try again — surfaced so the card can say "resumes ~3:47 pm"
    // in local time. Auto-expires: it's only shown while still in the future.
    public DateTimeOffset? RateLimitedUntilUtc { get; set; }

    // The Drive folder the finished outputs were archived into (the folder holding the source
    // manuscript), and when the archive completed. Set by the Archiving stage; null until then.
    public string? ArchiveFolderId { get; set; }
    public DateTimeOffset? ArchivedAtUtc { get; set; }

    // The in-flight Gemini batch job rendering this novel's panels (opt-in batch mode), and when it was
    // submitted. Null when no batch is outstanding; the worker polls this job and reconciles its results,
    // then clears it. Only one batch per novel is in flight at a time.
    public string? BatchJobId { get; set; }
    public DateTimeOffset? BatchSubmittedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public SourceText? Source { get; set; }
    public List<Chunk> Chunks { get; set; } = [];
    public List<Chapter> Chapters { get; set; } = [];
    public List<DesignRef> DesignRefs { get; set; } = [];
    public List<Volume> Volumes { get; set; } = [];

    // The per-chunk literary-analysis units (map step) and the consolidated book-analysis PDF record.
    public List<ChunkAnalysis> ChunkAnalyses { get; set; } = [];
    public BookAnalysis? Analysis { get; set; }
}

public class SourceText
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NovelId { get; set; }
    public string FullText { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
}

// A detected chapter of the manuscript, spanning [StartOffset, EndOffset) of the source text. Detected
// once at ingestion from heading heuristics (with a Claude pass only where the headings are genuinely
// ambiguous) so every report finding can cite where in the book it came from.
public class Chapter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NovelId { get; set; }

    // 1-based reading order. Not necessarily the number printed in the book (a prologue is 1 here).
    public int Number { get; set; }

    // The heading as it reads in the manuscript ("Chapter Seven", "XII", "THE LONG ROAD"), or a
    // synthesized label when the book has no headings at all.
    public string Label { get; set; } = string.Empty;

    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
}

public class Chunk : IRetryable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NovelId { get; set; }
    public int Ordinal { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public string Text { get; set; } = string.Empty;

    // The chapter this chunk starts in (0 when chapters weren't detected), and a human-readable label
    // covering every chapter the chunk overlaps ("Chapter 7", "Chapters 7–8"). Chunks are sized for the
    // model, not for the book, so one chunk routinely straddles a chapter break — the label carries the
    // whole span so a finding drawn from this chunk can cite it honestly.
    public int ChapterNumber { get; set; }
    public string ChapterLabel { get; set; } = string.Empty;

    public WorkStatus AnalysisStatus { get; set; } = WorkStatus.Pending;
    public string? PartialBibleJson { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }

    [NotMapped]
    public WorkStatus WorkState { get => AnalysisStatus; set => AnalysisStatus = value; }
}

// A canonical design-bible entity (character or setting), with its reference sheet render state.
public class DesignRef : IRetryable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NovelId { get; set; }
    public ReferenceKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CanonicalDescription { get; set; } = string.Empty;

    // Approximate physical size (a character's height/build, a setting's extent), carried from the
    // design bible so the reference sheet and panels keep the entity in proportion. "" when unknown.
    public string Scale { get; set; } = string.Empty;

    public string AliasesJson { get; set; } = "[]";

    // How many distinct chunks mentioned this entity, and whether it therefore earns a rendered
    // reference sheet. A novel names a great many one-off entities (a barman in one scene, a road
    // crossed once); rendering a sheet for each of them cost the owner's last book 126 image calls for
    // an ensemble of maybe a dozen. Only recurring entities — seen in MORE THAN ONE chunk — get a sheet.
    // Non-recurring entities stay in the design bible either way, so their descriptions still reach the
    // storyboard and panel prompts; they simply aren't drawn on their own.
    public int ChunkMentions { get; set; }
    public bool NeedsSheet { get; set; } = true;

    public WorkStatus ReferenceSheetStatus { get; set; } = WorkStatus.Pending;
    public string? ReferenceImagePath { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }

    [NotMapped]
    public WorkStatus WorkState { get => ReferenceSheetStatus; set => ReferenceSheetStatus = value; }
}

// A per-chunk literary-analysis unit (the "map" step of the revision report). Mirrors Chunk's
// resumable retry pattern; Ordinal matches the corresponding Chunk so its text can be looked up.
public class ChunkAnalysis : IRetryable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NovelId { get; set; }
    public int Ordinal { get; set; }
    public WorkStatus Status { get; set; } = WorkStatus.Pending;
    public string? NotesJson { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }

    [NotMapped]
    public WorkStatus WorkState { get => Status; set => Status = value; }
}

// The consolidated book-analysis (revision report) for a novel: the merged JSON from Claude and the
// rendered PDF's assembly state/path. One per novel (mirrors Volume's PDF record).
public class BookAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NovelId { get; set; }
    public string? ConsolidatedJson { get; set; }

    // The craft half of the report (line edits, prose/voice, dialogue, the scene-by-scene map),
    // serialized as a CraftReport. It is consolidated by a SECOND Claude call, so it is persisted
    // separately: the two halves then succeed, fail, and retry independently, and a failure in one
    // never re-bills the other. Null until that call has succeeded.
    public string? CraftJson { get; set; }

    // The web-searched verdicts on the checkable claims the per-chunk notes collected, serialized as a
    // JSON array of FactCheck. Null until the fact-check step runs; "[]" when there was nothing to check.
    // Persisted separately from ConsolidatedJson so the (billed, search-backed) verification survives a
    // re-consolidation and feeds into it.
    public string? FactChecksJson { get; set; }

    public WorkStatus PdfStatus { get; set; } = WorkStatus.Pending;
    public string? PdfPath { get; set; }

    // The Drive file id this report was archived to, once the Archiving stage has uploaded it. Set
    // together with clearing PdfPath: after archiving, Drive holds the copy.
    public string? ArchivedDriveFileId { get; set; }
}

public class Volume : IRetryable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NovelId { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public WorkStatus PdfStatus { get; set; } = WorkStatus.Pending;
    public string? PdfPath { get; set; }

    // The Drive file id this volume's PDF was archived to, once the Archiving stage has uploaded it.
    // Set together with clearing PdfPath: after archiving, Drive holds the copy.
    public string? ArchivedDriveFileId { get; set; }

    // How many of this volume's pages have already been appended to the growing on-disk PDF. Volume
    // assembly appends one page per worker call (plus the cover on the first), so a hang or restart
    // resumes mid-volume from this cursor instead of rebuilding every page in one memory-heavy pass.
    public int PdfPageCursor { get; set; }

    // Binding a volume can throw (a corrupt partial PDF, a vanished page image): rather than failing
    // permanently on the first error, assembly retries with backoff (bounded by MaxAttempts) before it
    // gives up, and a manual retry can revive a Failed volume. Mirrors Panel's resumable retry state.
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }

    public List<Page> Pages { get; set; } = [];

    [NotMapped]
    public WorkStatus WorkState { get => PdfStatus; set => PdfStatus = value; }
}

public class Page
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VolumeId { get; set; }
    public int ChapterNumber { get; set; }
    public int PageNumber { get; set; }
    public WorkStatus CompositionStatus { get; set; } = WorkStatus.Pending;
    public string? ComposedImagePath { get; set; }

    // A ~256px-wide downscale of the composed page, pre-generated so the
    // library list never decodes full pages on its refresh timer.
    public string? ThumbImagePath { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public List<Panel> Panels { get; set; } = [];
}

public class Panel : IRetryable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PageId { get; set; }
    public int PanelNumber { get; set; }
    public string VisualPrompt { get; set; } = string.Empty;
    public string? Dialogue { get; set; }
    public string? Caption { get; set; }

    // The camera framing this panel was storyboarded as, which sizes it on the page. Defaults to
    // Standard (0), so panels from before shot types existed lay out exactly as they did before.
    public ShotType ShotType { get; set; } = ShotType.Standard;

    // Set once this panel has been through a batch render (opt-in batch mode), so it is never re-batched:
    // a panel the batch failed or blocked returns to Pending and is re-rendered SYNCHRONOUSLY from then on
    // (the escalation ladder and its retries stay fast, not one batch turnaround per attempt).
    public bool BatchAttempted { get; set; }

    // The half-open span [SourceStartOffset, SourceEndOffset) of the novel's source text this panel was
    // storyboarded from — the absolute offsets of the storyboard window that produced it, not just the
    // chunk. Every panel from one window shares that window's span; the model doesn't sub-divide a
    // window per panel, so the window is the honest granularity. This is what makes "which panels does a
    // re-drafted manuscript actually change" computable: a panel whose source span still reads the same
    // in the new draft can be reused, only moved panels re-render. Zero-width on legacy rows the offsets
    // predate.
    public int SourceStartOffset { get; set; }
    public int SourceEndOffset { get; set; }

    // Names of the design-bible entities present in this panel; their reference sheets are
    // attached to the Gemini call to keep appearance consistent.
    public string ReferenceNamesJson { get; set; } = "[]";

    public WorkStatus Status { get; set; } = WorkStatus.Pending;
    public string? ImagePath { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }

    // Set when a render is safety-blocked: the Gemini HARM_CATEGORY_* names that tripped, serialized as
    // a JSON string array. On the user-approved re-render these are the only categories relaxed, and
    // only for that one call — the app's default thresholds are never lowered anywhere else.
    public string? BlockedCategoriesJson { get; set; }

    // The user approved rendering this blocked panel with the flagged category relaxed. Drives the
    // approved render path in RenderPanels; cleared once the panel finally renders (or is skipped).
    public bool ApprovedForRelaxedRender { get; set; }

    // The relaxed render still hit the model's non-adjustable floor, so Claude restaged the visual
    // prompt to imply rather than depict the content. Guards against reworking the same panel twice.
    public bool PromptRewrittenForSafety { get; set; }

    // The user chose to leave this panel out. It counts as done for stage advancement but is excluded
    // from page composition, so the page is drawn without it.
    public bool Skipped { get; set; }

    [NotMapped]
    public WorkStatus WorkState { get => Status; set => Status = value; }
}
