using System.ComponentModel.DataAnnotations.Schema;

namespace Folio.Domain.Model;

// Persistence entities (plain POCOs; EF configuration lives in the Infrastructure DbContext).
// Image bytes are kept on disk — these rows store paths, not blobs — and every unit of work
// carries a status so the background worker can resume purely from persisted state.

public class Novel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string GoogleDocId { get; set; } = string.Empty;
    public string GoogleDocName { get; set; } = string.Empty;

    // The book-wide art style for the per-scene evidence images, derived once from the prose + cast at
    // design-bible time (unless the user picked one). One cohesive style keeps characters and places
    // recognizable from scene to scene, which is part of the fidelity check.
    public string ArtStyle { get; set; } = ArtStyles.Default;
    public ProjectStage Stage { get; set; } = ProjectStage.Created;

    // Draft versioning. A "project" is one book the owner is revising; each draft is a fresh conversion
    // of a re-picked source doc, all sharing one ProjectId. The first draft's ProjectId is its own Id
    // (so a lone manuscript is a one-draft project), DraftNumber is 1, and PreviousDraftId is null. A
    // later draft carries the same ProjectId, the next DraftNumber, and PreviousDraftId pointing at the
    // draft it was made from — which is what the diff reads. Not a navigation property: drafts are
    // queried by ProjectId, and a self-referencing FK would drag the whole prior graph into the
    // change-tracker on every save.
    public Guid ProjectId { get; set; }
    public int DraftNumber { get; set; } = 1;
    public Guid? PreviousDraftId { get; set; }

    // Series linkage (Phase 5). A "project" is one book (a group of drafts sharing ProjectId); a "series"
    // groups multiple books. SeriesId is the series grouping key, denormalized onto every draft exactly as
    // ProjectId is — null means a standalone book. PreviousBookId is the book-level analogue of
    // PreviousDraftId: the specific prior-book DRAFT whose finished canon (design bible, continuity summary,
    // open findings) this book reuses, resolved at link time to the prior book's latest-completed draft.
    // SeriesSequence is this book's number in the series (1 = first), for the "Book N" badge. All three are
    // carried onto later drafts of a linked book, so every draft of book 2 stays linked to book 1.
    /// <summary>
    /// How long the manuscript is, measured once when it is read in.
    ///
    /// <para>A column rather than a derivation: the shelf draws every spine's width from it, and the
    /// only other source is splitting the whole manuscript, which is a full text read per book every
    /// time a list is drawn.</para>
    /// </summary>
    public int WordCount { get; set; }

    /// <summary>
    /// When the owner put this book on the shelf, or null while it is still on the desk.
    ///
    /// <para>Finishing a review and putting the book away are two different events. A review that
    /// completed overnight has not been read by anyone yet, and moving it to the shelf on the
    /// owner's behalf is the app deciding it is done with — so the pipeline never sets this, and
    /// <i>Put on the shelf</i> does.</para>
    /// </summary>
    public DateTimeOffset? ShelvedAtUtc { get; set; }

    public Guid? SeriesId { get; set; }

    /// <summary>
    /// What the owner calls this series, or null while nobody has said.
    ///
    /// <para>Stored rather than derived from the first book's title, which is the obvious rule and
    /// is wrong: a trilogy is often called something its first book is not, retitling book one
    /// should not rename the series, and a series can be named before anyone knows which book will
    /// open it. Null falls back to that title, so a series nobody has named still has a label.</para>
    ///
    /// <para>Denormalised onto every draft of every book in the series, like the three fields around
    /// it — renaming writes to all of them.</para>
    /// </summary>
    public string? SeriesName { get; set; }
    public Guid? PreviousBookId { get; set; }
    public int SeriesSequence { get; set; } = 1;

    // Set once the per-chunk extractions have been merged into canonical DesignRefs, so the merge
    // (which would otherwise reset reference-sheet state) runs exactly once.
    public bool DesignBibleConsolidated { get; set; }

    // Set once chapter boundaries have been detected and persisted at ingestion, so the (occasionally
    // Claude-assisted) detection runs exactly once per manuscript.
    public bool ChaptersDetected { get; set; }

    // Set once this draft's manuscript has fed the owner's shared voice profile at ingestion, so the
    // (billed) refine runs exactly once per draft even if the Ingesting stage is retried.
    public bool VoiceAnalyzed { get; set; }

    public string ContinuitySummary { get; set; } = string.Empty;

    // Consecutive failed attempts for the current once-per-manuscript reduce/report/archive step.
    // Bounded by RateLimitOptions.MaxAttempts so a persistently-failing call can't retry (and re-bill)
    // forever; reset to 0 on a successful step or a manual retry.
    public int StageAttempts { get; set; }

    public string? LastError { get; set; }

    // When the pipeline last deferred this manuscript because a provider rate-limited it (HTTP 429 /
    // quota), this is when it expects to try again — surfaced so the card can say "resumes ~3:47 pm"
    // in local time. Auto-expires: it's only shown while still in the future.
    public DateTimeOffset? RateLimitedUntilUtc { get; set; }

    // The Drive folder the finished report was archived into (the folder holding the source
    // manuscript), and when the archive completed. Set by the Archiving stage; null until then.
    public string? ArchiveFolderId { get; set; }
    public DateTimeOffset? ArchivedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public SourceText? Source { get; set; }
    public List<Chunk> Chunks { get; set; } = [];
    public List<Chapter> Chapters { get; set; } = [];
    public List<DesignRef> DesignRefs { get; set; } = [];
    public List<Scene> Scenes { get; set; } = [];
    public List<Finding> Findings { get; set; } = [];

    // The per-chunk literary-analysis units (map step) and the consolidated revision-report record.
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

// A canonical design-bible entity (character or setting), with its reference sheet render state. The
// reference sheet keeps a character/place looking the same across the per-scene evidence images, which
// is part of the fidelity check.
public class DesignRef : IRetryable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NovelId { get; set; }
    public ReferenceKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CanonicalDescription { get; set; } = string.Empty;

    // Approximate physical size (a character's height/build, a setting's extent), carried from the
    // design bible so the reference sheet and scene images keep the entity in proportion. "" when unknown.
    public string Scale { get; set; } = string.Empty;

    public string AliasesJson { get; set; } = "[]";

    // How many distinct chunks mentioned this entity, and whether it therefore earns a rendered
    // reference sheet. A novel names a great many one-off entities (a barman in one scene, a road
    // crossed once); rendering a sheet for each of them is wasteful. Only recurring entities — seen in
    // MORE THAN ONE chunk — get a sheet. Non-recurring entities stay in the design bible either way, so
    // their descriptions still reach the scene-image prompts; they simply aren't drawn on their own.
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

// A detected scene of the manuscript — the atomic unit of the review — spanning [StartOffset,
// EndOffset) of the source text, carrying exactly one wordless evidence image (the moment the prose is
// measured against). Scenes are detected at scene-break granularity, honoring authorial breaks and
// falling back to Claude within long unbroken chapters. The source span is what makes "which scenes did
// a re-drafted manuscript change" computable (the anchor Panel used to carry).
public class Scene : IRetryable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NovelId { get; set; }

    // 1-based reading order across the whole manuscript.
    public int Number { get; set; }

    // The chapter this scene belongs to, and a human-readable citation label ("Ch. 7 · Scene 3").
    public int ChapterNumber { get; set; }
    public string ChapterLabel { get; set; } = string.Empty;

    public int StartOffset { get; set; }
    public int EndOffset { get; set; }

    // Cross-draft identity (Phase 2). When this draft was made from a previous one, PreviousSceneId points
    // at the scene in that draft whose text this scene matched (null = a scene with no counterpart — new
    // this draft). Changed says whether the matched scene's text actually moved: false means the text is
    // unchanged, so the previous draft's rendered image is reused as-is (the image step skips it); true
    // means it changed, so a fresh image is rendered and the diff shows a before/after pair. Both are set
    // by SceneMatcher at seed time; on a first draft PreviousSceneId is null and Changed is false.
    public Guid? PreviousSceneId { get; set; }
    public bool Changed { get; set; }

    // The wordless-image prompt derived for this scene's emotional peak (the beat the prose is measured
    // against). Null until the image step has derived it.
    public string? ImagePrompt { get; set; }

    // The rendered evidence image's render state and on-disk path. A scene whose image the model refuses
    // even after retries ends Completed with a null ImagePath — the report notes "image unavailable" and
    // is never blocked.
    public WorkStatus ImageStatus { get; set; } = WorkStatus.Pending;
    public string? ImagePath { get; set; }

    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }

    [NotMapped]
    public WorkStatus WorkState { get => ImageStatus; set => ImageStatus = value; }
}

// The consolidated revision report for a manuscript: the merged JSON from Claude and the rendered PDF's
// assembly state/path. One per manuscript.
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

    // The authorial-voice-consistency read (dominant voice + divergent passages), serialized as a
    // VoiceConsistency. Its own call — it needs per-chapter prose samples rather than the notes — so it is
    // persisted separately and folded in at render. Null until it runs.
    public string? VoiceJson { get; set; }

    // The additive expansion read (Phase 3: scenes to add, show-vs-tell, length vs genre norm), serialized as
    // an ExpansionReport. Its own resumable call and column, folded in at render. Null until it runs.
    public string? ExpansionJson { get; set; }

    // Set once this draft's consolidated findings have been reconciled against the prior draft's threads and
    // persisted as Finding rows, so the (billed) reconciliation call runs exactly once — even for a draft
    // that produced zero findings, where no rows exist to signal completion.
    public bool FindingsReconciled { get; set; }

    public WorkStatus PdfStatus { get; set; } = WorkStatus.Pending;
    public string? PdfPath { get; set; }

    // The Drive file id this report was archived to, once the Archiving stage has uploaded it. Set
    // together with clearing PdfPath: after archiving, Drive holds the copy.
    public string? ArchivedDriveFileId { get; set; }

    // Set once reMarkable delivery has SETTLED (Phase 4) — either the report reached the tablet or delivery
    // was given up after its retry budget — so the best-effort target runs a bounded number of times and the
    // Archiving stage can advance. Parallel to ArchivedDriveFileId; the two delivery targets are independent.
    public bool DeliveredToRemarkable { get; set; }

    // The cross-book continuity read (Phase 5), serialized as a SeriesContinuity: where this book contradicts
    // the prior book's established canon, and the status of the prior book's carried-over open threads. Its own
    // resumable call and column, folded in at render like the craft/voice/expansion halves. Null until it runs;
    // it runs only for a book with a PreviousBookId — a standalone book leaves it null, so no section renders.
    public string? SeriesContinuityJson { get; set; }
}

// One story-integrity finding, promoted to a first-class row in Phase 2 (it lived only inside the report
// JSON before, with no identity). Each row is one OCCURRENCE of an issue in one draft; ThreadId is the
// issue's stable identity across every draft of the project, so "is this the same plot hole?" is
// answerable even after the wording moves. The reconciliation step (Claude, applied by FindingReconciler)
// assigns ThreadId and Status when it folds a draft's freshly-consolidated findings against the prior
// draft's threads. "Fixed" is not stored — it is derived from a thread that has no occurrence in a draft.
public class Finding
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // The draft this occurrence belongs to, and the project (denormalized from the draft's ProjectId) so a
    // thread's whole life can be queried without walking the draft chain.
    public Guid NovelId { get; set; }
    public Guid ProjectId { get; set; }

    // The stable cross-draft identity: two occurrences with the same ThreadId are the same issue over time.
    public Guid ThreadId { get; set; }

    public FindingCategory Category { get; set; }

    // The chapter citation and the observation text, carried over from the report's finding lists.
    public string Chapter { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;

    // This occurrence's state relative to the thread's history (see FindingStatus).
    public FindingStatus Status { get; set; }

    // The draft number in which this thread first appeared — so the history read can say "open since
    // draft 2" without re-deriving it from every occurrence.
    public int FirstDraftNumber { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
