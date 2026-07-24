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

    // Set once the per-chunk extractions have been merged into canonical DesignRefs, so the merge
    // (which would otherwise reset reference-sheet state) runs exactly once.
    public bool DesignBibleConsolidated { get; set; }

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

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public SourceText? Source { get; set; }
    public List<Chunk> Chunks { get; set; } = [];
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

public class Chunk : IRetryable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NovelId { get; set; }
    public int Ordinal { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public string Text { get; set; } = string.Empty;
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
    public WorkStatus PdfStatus { get; set; } = WorkStatus.Pending;
    public string? PdfPath { get; set; }
}

public class Volume : IRetryable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NovelId { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public WorkStatus PdfStatus { get; set; } = WorkStatus.Pending;
    public string? PdfPath { get; set; }

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
