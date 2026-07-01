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
    public string ContinuitySummary { get; set; } = string.Empty;

    public string? LastError { get; set; }
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

public class Volume
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NovelId { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public WorkStatus PdfStatus { get; set; } = WorkStatus.Pending;
    public string? PdfPath { get; set; }
    public List<Page> Pages { get; set; } = [];
}

public class Page
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VolumeId { get; set; }
    public int ChapterNumber { get; set; }
    public int PageNumber { get; set; }
    public WorkStatus CompositionStatus { get; set; } = WorkStatus.Pending;
    public string? ComposedImagePath { get; set; }
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

    [NotMapped]
    public WorkStatus WorkState { get => Status; set => Status = value; }
}
